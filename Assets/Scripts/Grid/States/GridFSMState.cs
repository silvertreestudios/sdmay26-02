using Unity.VisualScripting;
using UnityEngine;

public abstract class GridFSMState : IFSMState<GridFSMState>
{
    protected GridFSM fsm;
    public virtual void Enter(FiniteStateMachine<GridFSMState> fsm)
    {
        this.fsm = (GridFSM)fsm;
    }
    public abstract bool Exit();
    public abstract void Leftclick();
    public virtual void DoubleLeftclick() { }
    public abstract void Rightclick();
    public virtual void StateUpdate() { }
    public virtual void FixedStateUpdate() { }
}
