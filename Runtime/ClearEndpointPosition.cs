using UnityEngine;
#if AAO_VRCSDK3_AVATARS
using VRC.Dynamics;
#endif

namespace Anatawa12.AvatarOptimizer
{
#if AAO_VRCSDK3_AVATARS
    [AddComponentMenu("Avatar Optimizer/AAO Clear Endpoint Position")]
    [RequireComponent(typeof(VRCPhysBoneBase))]
#endif
    [DisallowMultipleComponent]
    [HelpURL("https://vpm.anatawa12.com/avatar-optimizer/ja/docs/reference/clear-endpoint-position/")]
    internal class ClearEndpointPosition : AvatarTagComponent
    {
    }
}
