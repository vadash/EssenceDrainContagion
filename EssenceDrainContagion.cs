using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.FilesInMemory;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using SharpDX;

namespace EssenceDrainContagion
{
    public class EssenceDrainContagion : BaseSettingsPlugin<EssenceDrainContagionSettings>
    {
        private readonly Stopwatch _aimTimer = Stopwatch.StartNew();
        private readonly List<Entity> _entities = new List<Entity>();
        private IDictionary<string, StatsDat.StatRecord> _statRecords;
        private bool _aiming;
        private Vector2 _oldMousePos;
        private HashSet<string> _ignoredMonsters;

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
            _statRecords = GameController.Files.Stats.records;
            return true;
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

        public override void Render()
        {
            if (_aimTimer.ElapsedMilliseconds < 100) return;

            try
            {
                if (!Input.IsKeyDown(Keys.RButton)) 
                    _oldMousePos = Input.MousePosition;
                if (Input.IsKeyDown(Keys.RButton)
                    && !GameController.Game.IngameState.IngameUi.InventoryPanel.IsVisible
                    && !GameController.Game.IngameState.IngameUi.OpenLeftPanel.IsVisible)
                {
                    _aiming = true;
                    var bestTarget = ScanValidMonsters()?.FirstOrDefault();
                    Attack(bestTarget);
                }
                if (!Input.IsKeyDown(Keys.RButton) && _aiming) 
                    Input.SetCursorPos(_oldMousePos);
                _aiming = false;
            }
            catch (Exception e)
            {
                LogError("Something went wrong? " + e, 5);
            }

            _aimTimer.Restart();
        }

        public override void EntityAdded(Entity entityWrapper) { _entities.Add(entityWrapper); }

        public override void EntityRemoved(Entity entityWrapper) { _entities.Remove(entityWrapper); }

        private void Attack(Tuple<float, Entity> bestTarget)
        {
            if (bestTarget == null) return;
            var position = GameController.Game.IngameState.Camera.WorldToScreen(bestTarget.Item2.Pos);
            var windowRectangle = GameController.Window.GetWindowRectangle();
            if (!position.IsInside(windowRectangle)) return;
            var offset = GameController.Window.GetWindowRectangle().TopLeft;
            Input.SetCursorPos(position + offset);
            Input.KeyPressRelease(bestTarget.Item2.HasBuff("contagion", true) ? Settings.EssenceDrainKey.Value : Settings.ContagionKey.Value);
        }

        private IEnumerable<Tuple<float, Entity>> ScanValidMonsters()
        {
            return _entities?.Where(x => 
                    x.HasComponent<Monster>() &&
                    x.IsAlive &&
                    x.IsHostile &&
                    x.GetStatValue("ignored_by_enemy_target_selection", _statRecords) == 0 &&
                    x.GetStatValue("cannot_die", _statRecords) == 0 &&
                    x.GetStatValue("cannot_be_damaged", _statRecords) == 0 &&
                    !_ignoredBuffs.Any(b => x.HasBuff(b)) &&
                    !_ignoredMonsters.Any(im => x.Path.ToLower().Contains(im))
                )
                .Select(x => new Tuple<float, Entity>(ComputeWeight(x), x))
                .Where(x => x.Item1 < Settings.AimRange)
                .OrderByDescending(x => x.Item1);
        }

        private float ComputeWeight(Entity entity)
        {
            var weight = 0;
            weight -= GameController.Player.DistanceFrom(entity) / 10;

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