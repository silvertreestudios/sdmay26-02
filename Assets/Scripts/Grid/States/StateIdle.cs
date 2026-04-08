using Unity.VisualScripting;
using UnityEngine;

namespace GridPrivate
{
    public class StateIdle : GridFSMState
    {
        public override bool Exit()
        {
            return true;
        }
       
    }
}
