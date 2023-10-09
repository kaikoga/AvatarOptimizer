using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;

#if AAO_VRCSDK3_AVATARS
using VRC.Dynamics;
#endif

using Object = UnityEngine.Object;

namespace Anatawa12.AvatarOptimizer.Processors.TraceAndOptimizes
{
    internal class FindUnusedObjects : Pass<FindUnusedObjects>
    {
        public override string DisplayName => "T&O: FindUnusedObjects";

        protected override void Execute(BuildContext context)
        {
            var state = context.GetState<TraceAndOptimizeState>();
            if (!state.RemoveUnusedObjects) return;

            new FindUnusedObjectsProcessor(context, state).Process();
        }
    }

    internal readonly struct FindUnusedObjectsProcessor
    {
        private readonly ImmutableModificationsContainer _modifications;
        private readonly BuildContext _context;
        private readonly HashSet<GameObject> _exclusions;
        private readonly bool _preserveEndBone;
        private readonly bool _useLegacyGC;
        private readonly bool _noConfigureMergeBone;
        private readonly bool _gcDebug;

        public FindUnusedObjectsProcessor(BuildContext context, TraceAndOptimizeState state)
        {
            _context = context;

            _modifications = state.Modifications;
            _preserveEndBone = state.PreserveEndBone;
            _useLegacyGC = state.UseLegacyGC;
            _noConfigureMergeBone = state.NoConfigureMergeBone;
            _gcDebug = state.GCDebug;
            _exclusions = state.Exclusions;

            _marked = new Dictionary<Component, ComponentDependencyCollector.DependencyType>();
            _processPending = new Queue<(Component, bool)>();
            _activeNessCache = new Dictionary<Component, bool?>();
        }

        public void Process()
        {
            if (_useLegacyGC)
                ProcessLegacy();
            else if (_gcDebug)
                CollectDataForGc();
            else
                ProcessNew();
        }

        // Mark & Sweep Variables
        private readonly Dictionary<Component, ComponentDependencyCollector.DependencyType> _marked;
        private readonly Queue<(Component, bool)> _processPending;
        private readonly Dictionary<Component, bool?> _activeNessCache;

        private bool? GetActiveness(Component component)
        {
            if (_activeNessCache.TryGetValue(component, out var activeness))
                return activeness;
            activeness = ComputeActiveness(component);
            _activeNessCache.Add(component, activeness);
            return activeness;
        }

        private bool? ComputeActiveness(Component component)
        {
            if (_context.AvatarRootTransform == component) return true;
            bool? parentActiveness;
            if (component is Transform t)
                parentActiveness = t.parent == null ? true : GetActiveness(t.parent);
            else
                parentActiveness = GetActiveness(component.transform);
            if (parentActiveness == false) return false;

            bool? activeness;
            switch (component)
            {
                case Transform transform:
                    var gameObject = transform.gameObject;
                    activeness = _modifications.GetConstantValue(gameObject, "m_IsActive", gameObject.activeSelf);
                    break;
                case Behaviour behaviour:
                    activeness = _modifications.GetConstantValue(behaviour, "m_Enabled", behaviour.enabled);
                    break;
                case Cloth cloth:
                    activeness = _modifications.GetConstantValue(cloth, "m_Enabled", cloth.enabled);
                    break;
                case Collider collider:
                    activeness = _modifications.GetConstantValue(collider, "m_Enabled", collider.enabled);
                    break;
                case LODGroup lodGroup:
                    activeness = _modifications.GetConstantValue(lodGroup, "m_Enabled", lodGroup.enabled);
                    break;
                case Renderer renderer:
                    activeness = _modifications.GetConstantValue(renderer, "m_Enabled", renderer.enabled);
                    break;
                // components without isEnable
                case CanvasRenderer _:
                case Joint _:
                case MeshFilter _:
                case OcclusionArea _:
                case OcclusionPortal _:
                case ParticleSystem _:
#if !UNITY_2021_3_OR_NEWER
                case ParticleSystemForceField _:
#endif
                case Rigidbody _:
                case Rigidbody2D _:
                case TextMesh _:
                case Tree _:
                case WindZone _:
#if AAO_VRCSDK3_AVATARS
                case UnityEngine.XR.WSA.WorldAnchor _:
                    activeness = true;
                    break;
#endif
                case Component _:
                case null:
                    // fallback: all components type should be proceed with above switch
                    activeness = null;
                    break;
            }

            if (activeness == false) return false;
            if (parentActiveness == true && activeness == true) return true;

            return null;
        }

        private void MarkComponent(Component component,
            bool ifTargetCanBeEnabled,
            ComponentDependencyCollector.DependencyType type)
        {
            bool? activeness = GetActiveness(component);

            if (ifTargetCanBeEnabled && activeness == false)
                return; // The Target is not active so not dependency

            if (_marked.TryGetValue(component, out var existingFlags))
            {
                _marked[component] = existingFlags | type;
            }
            else
            {
                _processPending.Enqueue((component, activeness != false));
                _marked.Add(component, type);
            }
        }

        private void ProcessNew()
        {
            MarkAndSweep();
            if (!_noConfigureMergeBone) ConfigureMergeBone();
        }

        private void MarkAndSweep()
        {
            // first, collect usages
            var collector = new ComponentDependencyCollector(_context, _preserveEndBone);
            collector.CollectAllUsages();

            // then, mark and sweep.

            // entrypoint for mark & sweep is active-able GameObjects
            foreach (var gameObject in CollectAllActiveAbleGameObjects())
            foreach (var component in gameObject.GetComponents<Component>())
                if (collector.GetDependencies(component).EntrypointComponent)
                    MarkComponent(component, true, ComponentDependencyCollector.DependencyType.Normal);

            // excluded GameObjects must be exists
            foreach (var gameObject in _exclusions)
            foreach (var component in gameObject.GetComponents<Component>())
                MarkComponent(component, true, ComponentDependencyCollector.DependencyType.Normal);

            while (_processPending.Count != 0)
            {
                var (component, canBeActive) = _processPending.Dequeue();
                var dependencies = collector.TryGetDependencies(component);
                if (dependencies == null) continue; // not part of this Hierarchy Tree

                foreach (var (dependency, flags) in dependencies.Dependencies)
                {
                    var ifActive =
                        (flags.flags & ComponentDependencyCollector.DependencyFlags.EvenIfThisIsDisabled) == 0;
                    if (ifActive && !canBeActive) continue;
                    var ifTargetCanBeEnabled =
                        (flags.flags & ComponentDependencyCollector.DependencyFlags.EvenIfTargetIsDisabled) == 0;
                    MarkComponent(dependency, ifTargetCanBeEnabled, flags.type);
                }
            }

            foreach (var component in _context.GetComponents<Component>())
            {
                // null values are ignored
                if (!component) continue;

                if (component is Transform)
                {
                    // Treat Transform Component as GameObject because they are two sides of the same coin
                    if (!_marked.ContainsKey(component))
                        Object.DestroyImmediate(component.gameObject);
                }
                else
                {
                    if (!_marked.ContainsKey(component))
                        Object.DestroyImmediate(component);
                }
            }
        }

        private void CollectDataForGc()
        {
            // first, collect usages
            var collector = new ComponentDependencyCollector(_context, _preserveEndBone);
            collector.CollectAllUsages();

            var componentDataMap = new Dictionary<Component, GCData.ComponentData>();

            foreach (var component in _context.GetComponents<Component>())
            {
                var componentData = new GCData.ComponentData { component = component };
                componentDataMap.Add(component, componentData);

                switch (ComputeActiveness(component))
                {
                    case false:
                        componentData.activeness = GCData.ActiveNess.False;
                        break;
                    case true:
                        componentData.activeness = GCData.ActiveNess.True;
                        break;
                    case null:
                        componentData.activeness = GCData.ActiveNess.Variable;
                        break;
                }

                var dependencies = collector.GetDependencies(component);
                foreach (var (key, (flags, type)) in dependencies.Dependencies)
                    componentData.dependencies.Add(new GCData.DependencyInfo(key, flags, type));
            }

            foreach (var gameObject in CollectAllActiveAbleGameObjects())
            foreach (var component in gameObject.GetComponents<Component>())
                if (collector.GetDependencies(component).EntrypointComponent)
                    componentDataMap[component].entrypoint = true;

            foreach (var gameObject in _exclusions)
            foreach (var component in gameObject.GetComponents<Component>())
                componentDataMap[component].entrypoint = true;

            foreach (var component in _context.GetComponents<Component>())
            {
                var dependencies = collector.GetDependencies(component);
                foreach (var (key, (flags, type)) in dependencies.Dependencies)
                    if (componentDataMap.TryGetValue(key, out var info))
                        info.dependants.Add(new GCData.DependencyInfo(component, flags, type));
            }

            
            foreach (var component in _context.GetComponents<Component>())
                component.gameObject.GetOrAddComponent<GCData>().data.Add(componentDataMap[component]);
            _context.AvatarRootObject.AddComponent<GCDataRoot>();
        }

        class GCDataRoot : MonoBehaviour
        {
        }

        [CustomEditor(typeof(GCDataRoot))]
        class GCDataRootEditor : Editor
        {
            public override void OnInspectorGUI()
            {
                if (GUILayout.Button("Copy All Data"))
                {
                    var root = ((Component)target).gameObject;
                    var collect = new StringBuilder();
                    foreach (var gcData in root.GetComponentsInChildren<GCData>())
                    {
                        collect.Append(RuntimeUtil.RelativePath(root, gcData.gameObject)).Append(":\n");

                        foreach (var componentData in gcData.data.Where(componentData => componentData.component))
                        {
                            collect.Append("  ").Append(componentData.component.GetType().Name).Append(":\n");
                            collect.Append("    ActiveNess: ").Append(componentData.activeness).Append('\n');
                            collect.Append("    Dependencies:\n");
                            var list = new List<string>();
                            foreach (var dependencyInfo in componentData.dependencies.Where(x => x.component))
                            {
                                var path = RuntimeUtil.RelativePath(root, dependencyInfo.component.gameObject);
                                var types = dependencyInfo.component.GetType().Name;
                                list.Add($"{path}({types})({dependencyInfo.type},{dependencyInfo.flags})");
                            }
                            list.Sort();
                            foreach (var line in list)
                                collect.Append("      ").Append(line).Append("\n");
                        }

                        collect.Append("\n");
                    }

                    GUIUtility.systemCopyBuffer = collect.ToString();
                }
            }
        }

        class GCData : MonoBehaviour
        {
            public List<ComponentData> data = new List<ComponentData>();

            [Serializable]
            public class ComponentData
            {
                public Component component;
                public ActiveNess activeness;
                public bool entrypoint;
                public List<DependencyInfo> dependencies = new List<DependencyInfo>();
                public List<DependencyInfo> dependants = new List<DependencyInfo>();
            }

            [Serializable]
            public class DependencyInfo
            {
                public Component component;
                public ComponentDependencyCollector.DependencyFlags flags;
                public ComponentDependencyCollector.DependencyType type;

                public DependencyInfo(Component component, ComponentDependencyCollector.DependencyFlags flags,
                    ComponentDependencyCollector.DependencyType type)
                {
                    this.component = component;
                    this.flags = flags;
                    this.type = type;
                }
            }

            public enum ActiveNess
            {
                False,
                True,
                Variable
            }
        }

        private void ConfigureMergeBone()
        {
            ConfigureRecursive(this, _context.AvatarRootTransform, _modifications);

            // returns true if merged
            bool ConfigureRecursive(in FindUnusedObjectsProcessor processor, Transform transform,
                ImmutableModificationsContainer modifications)
            {
                var mergedChildren = true;
                foreach (var child in transform.DirectChildrenEnumerable())
                    mergedChildren &= ConfigureRecursive(processor, child, modifications);

                const ComponentDependencyCollector.DependencyType AllowedUsages =
                    ComponentDependencyCollector.DependencyType.Bone
                    | ComponentDependencyCollector.DependencyType.Parent
                    | ComponentDependencyCollector.DependencyType.ComponentToTransform;

                // Already Merged
                if (transform.GetComponent<MergeBone>()) return true;
                // Components must be Transform Only
                if (transform.GetComponents<Component>().Length != 1) return false;
                // The bone cannot be used generally
                if ((processor._marked[transform] & ~AllowedUsages) != 0) return false;
                // must not be animated
                if (TransformAnimated(transform, modifications)) return false;

                if (!mergedChildren)
                {
                    if (GameObjectAnimated(transform, modifications)) return false;

                    var localScale = transform.localScale;
                    var identityTransform = localScale == Vector3.one && transform.localPosition == Vector3.zero &&
                                            transform.localRotation == Quaternion.identity;

                    if (!identityTransform)
                    {
                        var childrenTransformAnimated =
                            transform.DirectChildrenEnumerable().Any(x => TransformAnimated(x, modifications));
                        if (childrenTransformAnimated)
                            // if this is not identity transform, animating children is not good
                            return false;

                        if (!MergeBoneProcessor.ScaledEvenly(localScale))
                            // non even scaling is not possible to reproduce in children
                            return false;
                    }
                }

                if (!transform.gameObject.GetComponent<MergeBone>())
                    transform.gameObject.AddComponent<MergeBone>().avoidNameConflict = true;

                return true;
            }

            bool TransformAnimated(Transform transform, ImmutableModificationsContainer modifications)
            {
                var transformProperties = modifications.GetModifiedProperties(transform);
                if (transformProperties.Count != 0)
                {
                    // TODO: constant animation detection
                    foreach (var transformProperty in TransformProperties)
                        if (transformProperties.ContainsKey(transformProperty))
                            return true;
                }

                return false;
            }

            bool GameObjectAnimated(Transform transform, ImmutableModificationsContainer modifications)
            {
                var objectProperties = modifications.GetModifiedProperties(transform.gameObject);

                if (objectProperties.ContainsKey("m_IsActive"))
                    return true;

                return false;
            }
        }

        private static readonly string[] TransformProperties =
        {
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
            "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", 
            "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z", 
            "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "localEulerAnglesRaw.z"
        };

        private IEnumerable<GameObject> CollectAllActiveAbleGameObjects()
        {
            var queue = new Queue<GameObject>();
            queue.Enqueue(_context.AvatarRootTransform.gameObject);

            while (queue.Count != 0)
            {
                var gameObject = queue.Dequeue();
                var activeNess = _modifications.GetConstantValue(gameObject, "m_IsActive", gameObject.activeSelf);
                switch (activeNess)
                {
                    case null:
                    case true:
                        // This GameObject can be active
                        yield return gameObject;
                        foreach (var transform in gameObject.transform.DirectChildrenEnumerable())
                            queue.Enqueue(transform.gameObject);
                        break;
                    case false:
                        // This GameObject and their children will never be active
                        break;
                }
            }
        }

        private void ProcessLegacy() {
            // mark & sweep
            var gameObjects = new HashSet<GameObject>(_context.GetComponents<Transform>().Select(x => x.gameObject));
            var referenced = new HashSet<GameObject>();
            var newReferenced = new Queue<GameObject>();

            void AddGameObject(GameObject gameObject)
            {
                if (gameObject && gameObjects.Contains(gameObject) && referenced.Add(gameObject))
                    newReferenced.Enqueue(gameObject);
            }

            // entry points: active GameObjects
            foreach (var component in gameObjects.Where(x => x.activeInHierarchy))
                AddGameObject(component);

            // entry points: modified enable/disable
            foreach (var keyValuePair in _modifications.ModifiedProperties)
            {
                // TODO: if the any of parent is inactive and kept, it should not be assumed as 
                if (!keyValuePair.Key.AsGameObject(out var gameObject)) continue;
                if (!keyValuePair.Value.TryGetValue("m_IsActive", out _)) continue;

                // TODO: if the child is not activeSelf, it should not be assumed as entry point.
                foreach (var transform in gameObject.GetComponentsInChildren<Transform>())
                    AddGameObject(transform.gameObject);
            }

            // entry points: active GameObjects
            foreach (var gameObject in _exclusions)
                AddGameObject(gameObject);

            while (newReferenced.Count != 0)
            {
                var gameObject = newReferenced.Dequeue();

                foreach (var component in gameObject.GetComponents<Component>())
                {
                    if (component is Transform transform)
                    {
                        if (transform.parent)
                            AddGameObject(transform.parent.gameObject);
                        continue;
                    }

#if AAO_VRCSDK3_AVATARS
                    if (component is VRCPhysBoneBase)
                    {
                        foreach (var child in component.GetComponentsInChildren<Transform>(true))
                            AddGameObject(child.gameObject);
                    }
#endif

                    using (var serialized = new SerializedObject(component))
                    {
                        foreach (var iter in serialized.ObjectReferenceProperties())
                        {
                            var value = iter.objectReferenceValue;
                            if (value is Component c && !EditorUtility.IsPersistent(value))
                                AddGameObject(c.gameObject);
                        }
                    }
                }
            }

            // sweep
            foreach (var gameObject in gameObjects.Where(x => !referenced.Contains(x)))
            {
                if (gameObject)
                    Object.DestroyImmediate(gameObject);
            }
        }
    }
}
