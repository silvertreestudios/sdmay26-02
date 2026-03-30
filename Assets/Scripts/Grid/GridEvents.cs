using System.Collections.Generic;
using UnityEngine;

namespace GridPrivate
{
    /// <summary>Triggered on hover</summary>
    public class OnHover : StaticUnityEvent<OnHover, Vector3Int> { }
    /// <summary>Triggered on no hover</summary>
    public class OnHoverEnd : StaticUnityEvent<OnHover> { }
    /// <summary>Triggered to highlight range</summary>
    public class OnHighlightRange : StaticUnityEvent<OnHighlightRange, List<Vector3Int>> { }
}
