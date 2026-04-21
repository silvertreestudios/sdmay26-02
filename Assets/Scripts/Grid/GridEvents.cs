using System.Collections.Generic;
using UnityEngine;

namespace GridPrivate
{
    /// <summary>
    /// Triggered on hover. The list can be used for highlighting multiple cells,
    /// Example: attacks with spash effects.
    /// </summary>
    public class OnHover : StaticUnityEvent<OnHover, List<Vector3Int>> { }
    /// <summary>Triggered on no hover</summary>
    public class OnHoverEnd : StaticUnityEvent<OnHoverEnd> { }
    /// <summary>Triggered to highlight range</summary>
    public class OnHighlightRange : StaticUnityEvent<OnHighlightRange, List<Vector3Int>> { }
    /// <summary>Triggered on end highlight range</summary>
    public class OnHighlightRangeEnd : StaticUnityEvent<OnHighlightRangeEnd> { }

    /// <summary>Triggered on succesful action Cancelation</summary>
    public class OnActionCancel : StaticUnityEvent<OnActionCancel> { }

    /// <summary>Triggered on previewing a path</summary>
    public class OnPreviewPath : StaticUnityEvent<OnPreviewPath, List<Vector3Int>> { }
}
