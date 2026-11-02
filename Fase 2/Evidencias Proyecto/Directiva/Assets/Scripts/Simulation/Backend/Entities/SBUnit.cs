using DMath;

namespace DSimulation
{
    class SBUnit : SBEntity
    {
        public SBUnit(FixedVector2 position, Fixed rotation) : base(
            position, rotation, 
            SConstants.UnitRadius, SConstants.UnitPriority, 
            SConstants.UnitHealth, SConstants.UnitShields, SConstants.UnitShieldRegen
            )
        {
            
        }
    }
}