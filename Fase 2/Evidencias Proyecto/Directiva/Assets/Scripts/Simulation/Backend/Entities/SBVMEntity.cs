using DMath;

namespace DSimulation
{
    public class SBVMEntity : SBEntity
    {
        public readonly int side;
        /// <summary> How much damage the entity causes per attack. </summary>
        internal readonly Fixed damage;
        /// <summary> How far away the entity can shoot. </summary>
        internal readonly Fixed range;
        /// <summary> How long it takes for the entity to be ready to attack again. Lower is beter. </summary>
        internal readonly Fixed attackSpeed;

        public SBVMEntity(
            int side,
            FixedVector2 position, 
            Fixed rotation, 
            Fixed radius, 
            int collisionPriority, 
            Fixed maxHealth, 
            Fixed maxShields, 
            Fixed shieldRegen) : base(
                position, rotation,
                radius, collisionPriority,
                maxHealth, maxShields, shieldRegen
            )
        {
            this.side = side;
        }
    }
}