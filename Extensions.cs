using System;
using System.Linq;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using SharpDX;

namespace EssenceDrainContagion
{
    public static class Extensions
    {
        private const int PixelBorder = 3;

        public static bool HasBuff(this Entity entity, string buff, bool contains = false)
        {
            return entity.HasComponent<Life>() &&
                   entity.GetComponent<Life>().Buffs.Any(b => contains ? b.Name.Contains(buff) : b.Name == buff);
        }

        public static bool HaveStat(Entity entity, GameStat stat)
        {
            try
            {
                var result = entity?.GetComponent<Stats>()?.StatDictionary?[stat];
                return result > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        public static bool IsInside(this Vector2 position, RectangleF container)
        {
            return position.Y + PixelBorder < container.Bottom
                   && position.Y - PixelBorder > container.Top
                   && position.X + PixelBorder < container.Right
                   && position.X - PixelBorder > container.Left;
        }

        public static int DistanceFrom(this Entity fromEntity, Entity toEntity)
        {
            var Object = toEntity.GetComponent<Render>();
            try
            {
                return Convert.ToInt32(Vector3.Distance(fromEntity.Pos, Object.Pos));
            }
            catch
            {
                return Int32.MaxValue;
            }
        }
    }
}
