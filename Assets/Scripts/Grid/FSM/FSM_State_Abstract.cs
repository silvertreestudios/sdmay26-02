using Unity.VisualScripting;
using UnityEngine;

public abstract class FSM_State_Abstract
{
    public abstract void Leftclick();
    public virtual void DoubleLeftclick() { }
    public abstract void Rightclick();
    public virtual void StateUpdate() { }
    public virtual void FixedStateUpdate() { }
}
