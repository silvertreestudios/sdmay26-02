using System.Collections;
using UnityEngine;

public interface IFSMState<T> where T : IFSMState<T>
{
    public void Enter(FiniteStateMachine<T> fsm);
    public void Exit();
}