using GridPrivate;
using GridPublic;
using UnityEngine;

namespace GridPublic
{
    public class Token : MonoBehaviour
    {
        void Awake()
        {
            GridAPI grid = GridAPI.GetInstance();
            GridAPIPrivate priv = (GridAPIPrivate)grid;
            priv.AddToken(this.gameObject);
        }
    }
}
