namespace GridPrivate
{
    /// <summary>Accepts map input while no manual action-selection state is active.</summary>
    public class StateIdle : GridFSMState
    {
        /// <inheritdoc/>
        public override void Rightclick() => fsm.TryCancelIdleOperation();
    }
}
