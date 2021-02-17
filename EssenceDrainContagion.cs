using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using SharpDX;

namespace EssenceDrainContagion
{
    public class EssenceDrainContagion : BaseSettingsPlugin<EssenceDrainContagionSettings>
    {
        private bool _aiming;
        private Vector2 _oldMousePos;
        private HashSet<string> _ignoredMonsters;
        private Coroutine _mainCoroutine;
        private Tuple<float, Entity> _currentTarget;
        private Stopwatch _lastTargetSwap = new Stopwatch();

        private readonly string[] _ignoredBuffs = {
            "capture_monster_captured",
            "capture_monster_disappearing"
        };

        private readonly string[] _lightLessGrub =
            {
                "Metadata/Monsters/HuhuGrub/AbyssGrubMobile",
                "Metadata/Monsters/HuhuGrub/AbyssGrubMobileMinion"
        };
        
        private readonly string[] _raisedZombie =
        {
                "Metadata/Monsters/RaisedZombies/RaisedZombieStandard",
                "Metadata/Monsters/RaisedZombies/RaisedZombieMummy",
                "Metadata/Monsters/RaisedZombies/NecromancerRaisedZombieStandard"
        };

        private readonly string[] _summonedSkeleton =
        {
                "Metadata/Monsters/RaisedSkeletons/RaisedSkeletonStandard",
                "Metadata/Monsters/RaisedSkeletons/RaisedSkeletonStatue",
                "Metadata/Monsters/RaisedSkeletons/RaisedSkeletonMannequin",
                "Metadata/Monsters/RaisedSkeletons/RaisedSkeletonStatueMale",
                "Metadata/Monsters/RaisedSkeletons/RaisedSkeletonStatueGold",
                "Metadata/Monsters/RaisedSkeletons/RaisedSkeletonStatueGoldMale",
                "Metadata/Monsters/RaisedSkeletons/NecromancerRaisedSkeletonStandard",
                "Metadata/Monsters/RaisedSkeletons/TalismanRaisedSkeletonStandard"
        };

        public override bool Initialise()
        {
            LoadIgnoredMonsters($@"{DirectoryFullName}\Ignored Monsters.txt");
            Input.RegisterKey(Settings.AimKey);
            _mainCoroutine = new Coroutine(
                MainCoroutine(),
                this,
                "EDC");
            Core.ParallelRunner.Run(_mainCoroutine);
            return true;
        }

        private IEnumerator MainCoroutine()
        {
            while (true)
            {
                try
                {
                    if (_currentTarget == null ||
                        !ValidTarget(_currentTarget?.Item2))
                    {
                        _currentTarget = ScanValidMonsters()?.FirstOrDefault();
                        _lastTargetSwap.Restart();
                    }
                    else if (_lastTargetSwap.ElapsedMilliseconds > 100)
                    {
                        var best = ScanValidMonsters()?.FirstOrDefault();
                        if (best?.Item1 > 1.2f * _currentTarget?.Item1) _currentTarget = best;
                        _lastTargetSwap.Restart();
                    }
                }
                catch
                {
                    // ignored
                }

                if (!Input.IsKeyDown(Settings.AimKey)) 
                    _oldMousePos = Input.MousePosition;
                if (Input.IsKeyDown(Settings.AimKey)
                    && !GameController.Game.IngameState.IngameUi.InventoryPanel.IsVisible
                    && !GameController.Game.IngameState.IngameUi.OpenLeftPanel.IsVisible)
                {
                    _aiming = true;
                    yield return Attack();
                }

                if (!Input.IsKeyDown(Settings.AimKey) && _aiming)
                {
                    Input.SetCursorPos(_oldMousePos);
                    _aiming = false;
                }

                yield return new WaitTime(10);
            }
            // ReSharper disable once IteratorNeverReturns
        }

        private bool ValidTarget(Entity entity)
        {
            try
            {
                return entity != null &&
                       entity.IsValid &&
                       entity.IsAlive &&
                       entity.HasComponent<Monster>() &&
                       entity.IsHostile &&
                       entity.HasComponent<Targetable>() &&
                       entity.GetComponent<Targetable>().isTargetable &&
                       entity.HasComponent<Life>() &&
                       entity.GetComponent<Life>().CurHP > 0 &&
                       entity.DistancePlayer < Settings.AimRangeGrid &&
                       GameController.Window.GetWindowRectangleTimeCache.Contains(
                           GameController.Game.IngameState.Camera.WorldToScreen(entity.Pos));
            }
            catch
            {
                return false;
            }
        }

        public override void Render()
        {
            if (_currentTarget != null)
            {
                var position = GameController.Game.IngameState.Camera.WorldToScreen(_currentTarget.Item2.Pos);
                Graphics.DrawFrame(position, position.Translate(20, 20), Color.Chocolate, 3);
            }
            base.Render();
        }

        private void LoadIgnoredMonsters(string fileName)
        {
            _ignoredMonsters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(fileName))
            {
                LogError($@"Failed to find {fileName}", 10);
                return;
            }

            foreach (var line in File.ReadAllLines(fileName))
                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    _ignoredMonsters.Add(line.Trim().ToLower());
        }

        private IEnumerator Attack()
        {
            if (_currentTarget == null) yield break;
            var position = GameController.Game.IngameState.Camera.WorldToScreen(_currentTarget.Item2.Pos);
            Input.SetCursorPos(position);
            yield return Input.KeyPress(_currentTarget.Item2.HasBuff("contagion", true) ? Settings.EssenceDrainKey.Value : Settings.ContagionKey.Value);
        }

        private IEnumerable<Tuple<float, Entity>> ScanValidMonsters()
        {
            var queue =
                from entity in GameController?.EntityListWrapper?.ValidEntitiesByType?[EntityType.Monster]
                where ValidTarget(entity)
                where !Extensions.HaveStat(entity, GameStat.CannotDie) &&
                      !Extensions.HaveStat(entity, GameStat.CannotBeDamaged) &&
                      !Extensions.HaveStat(entity, GameStat.IgnoredByEnemyTargetSelection) &&
                      !_ignoredBuffs.Any(b => entity.HasBuff(b)) &&
                      !_ignoredMonsters.Any(im => entity.Path.ToLower().Contains(im))
                let weight = ComputeWeight(entity)
                orderby weight descending
                select new Tuple<float, Entity>(weight, entity);
            return queue;
        }

        private float ComputeWeight(Entity entity)
        {
            var weight = 0;
            if (Settings.ClosestToMouse)
            {
                var p1 = Input.MousePosition;
                var p2 = GameController.Game.IngameState.Camera.WorldToScreen(entity.Pos);
                weight -= (int) (p1.Distance(p2) / 10f);
            }
            else
            {
                var p1 = GameController.Game.IngameState.Camera.WorldToScreen(GameController.Player.Pos);
                var p2 = GameController.Game.IngameState.Camera.WorldToScreen(entity.Pos);
                weight -= (int) (p1.Distance(p2) / 10f);
            }

            if (entity.GetComponent<Life>().HasBuff("contagion")) weight += Settings.HasContagionWeight;
            if (entity.GetComponent<Life>().HasBuff("capture_monster_trapped")) weight += Settings.capture_monster_trapped;
            if (entity.GetComponent<Life>().HasBuff("harbinger_minion_new")) weight += Settings.HarbingerMinionWeight;
            if (entity.GetComponent<Life>().HasBuff("capture_monster_enraged")) weight += Settings.capture_monster_enraged;
            if (entity.Path.Contains("/BeastHeart")) weight += Settings.BeastHearts;
            if (entity.Path == "Metadata/Monsters/Tukohama/TukohamaShieldTotem") weight += Settings.TukohamaShieldTotem;

            switch (entity.GetComponent<ObjectMagicProperties>().Rarity)
            {
                case MonsterRarity.Unique:
                    weight += Settings.UniqueRarityWeight;
                    break;
                case MonsterRarity.Rare:
                    weight += Settings.RareRarityWeight;
                    break;
                case MonsterRarity.Magic:
                    weight += Settings.MagicRarityWeight;
                    break;
                case MonsterRarity.White:
                    weight += Settings.NormalRarityWeight;
                    break;
                case MonsterRarity.Error:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (entity.HasComponent<DiesAfterTime>()) weight += Settings.DiesAfterTime;
            if (_summonedSkeleton.Any(path => entity.Path == path)) weight += Settings.SummonedSkeoton;
            if (_raisedZombie.Any(path => entity.Path == path)) weight += Settings.RaisedZombie;
            if (_lightLessGrub.Any(path => entity.Path == path)) weight += Settings.LightlessGrub;
            if (entity.Path.Contains("TaniwhaTail")) weight += Settings.TaniwhaTail;
            return weight;
        }
    }

}