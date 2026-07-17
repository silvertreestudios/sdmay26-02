using GridPrivate;
using GridPublic;
using UnityEngine;

namespace GridPublic
{
    public class Token : MonoBehaviour
    {
        void Awake()
        {
            if (GridAPI.TryGetInstance(out GridAPI grid) && grid is GridAPIPrivate priv)
                priv.AddToken(this.gameObject);
        }
    }
}
