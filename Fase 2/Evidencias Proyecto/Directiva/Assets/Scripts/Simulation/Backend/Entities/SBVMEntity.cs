using DMath;
using DSEvents;

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
        private Fixed lastAttack;


        protected void NotifyAttack(SBEntity target)
        {
            simulation.NotifyEvent(new EventAttack(id, target.id));
        }

        protected virtual bool CanAttack(SBEntity target)
        {
            return target.alive // El objetivo aún está vivo
                && simulation.Time - lastAttack >= attackSpeed // El cooldown de ataque ya acabó
                && (target.Position - Position).SqrMagnitude <= range * range; // El objetivo está a nuestro alcance
        }

        protected virtual void Attack(SBEntity target)
        {
            if (!CanAttack(target))
                return;

            NotifyAttack(target);
            lastAttack = simulation.Time;
            target.Damage(damage);
        }

        public SBVMEntity(
            int side,
            Fixed damage,
            Fixed range,
            Fixed attackSpeed,
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
            this.damage = damage;
            this.range = range;
            this.attackSpeed = attackSpeed;
            this.lastAttack = -attackSpeed;
        }
    }
}