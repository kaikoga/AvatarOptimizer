using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using JetBrains.Annotations;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Assertions;
using Debug = System.Diagnostics.Debug;

namespace Anatawa12.AvatarOptimizer.AnimatorParsersV2
{
    /// <summary>
    /// This class represents a node in the property modification tree.
    ///
    /// In AnimatorParser V2, Modifications of each property are represented as a tree to make it possible to
    /// remove modifications of a property.
    ///
    /// This class is the abstract class for the nodes.
    ///
    /// Most nodes are immutable but some nodes are mutable.
    /// </summary>
    internal abstract class PropModNode<T> : IErrorContext
    {
        /// <summary>
        /// Returns the constant value of this node. If not constant, throws an exception.
        /// </summary>
        /// <throws cref="InvalidConstraintException">If not constant, or not active</throws>
        public abstract T ConstantValue { get; }

        /// <summary>
        /// Returns true if this node is constant. For inactive nodes, this returns false.
        /// </summary>
        public abstract bool IsConstant { get; }

        /// <summary>
        /// Returns true if this node is always applied. For inactive nodes, this returns false.
        /// </summary>
        public abstract bool AppliedAlways { get; }
        public abstract IEnumerable<ObjectReference> ContextReferences { get; }
    }

    internal interface INodeContainer
    {
        IReadOnlyDictionary<(ComponentOrGameObject target, string prop), PropModNode<float>> FloatNodes { get; }
    }

    internal interface INodeContainer<TFloatNode>
    {
        Dictionary<(ComponentOrGameObject target, string prop), TFloatNode> FloatNodes { get; }
    }

    internal class AnimatorControllerNodeContainer : INodeContainer<AnimatorControllerPropModNode<float>>, INodeContainer
    {
        public Dictionary<(ComponentOrGameObject target, string prop), AnimatorControllerPropModNode<float>> FloatNodes { get; } =
            new Dictionary<(ComponentOrGameObject, string), AnimatorControllerPropModNode<float>>();

        IReadOnlyDictionary<(ComponentOrGameObject target, string prop), PropModNode<float>> INodeContainer.
            FloatNodes => Utils.CastDic<PropModNode<float>>().CastedDic(FloatNodes);

        public void Add(ComponentOrGameObject target, string prop, [NotNull] AnimatorControllerPropModNode<float> node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            FloatNodes.Add((target, prop), node);
        }
    }

    internal class RootPropModNodeContainer : INodeContainer<RootPropModNode<float>>, INodeContainer
    {
        public Dictionary<(ComponentOrGameObject target, string prop), RootPropModNode<float>> FloatNodes { get; } =
            new Dictionary<(ComponentOrGameObject, string), RootPropModNode<float>>();

        IReadOnlyDictionary<(ComponentOrGameObject target, string prop), PropModNode<float>> INodeContainer.FloatNodes => 
            Utils.CastDic<PropModNode<float>>().CastedDic(FloatNodes);

        public void Add(ComponentNodeContainer container)
        {
            foreach (var (key, value) in container.FloatNodes)
            {
                if (!FloatNodes.TryGetValue(key, out var node))
                    FloatNodes.Add(key, node = new RootPropModNode<float>());
                node.Add(value);
            }
        }

        public bool? GetConstantValue(ComponentOrGameObject gameObject, string property, bool gameObjectActiveSelf)
        {
            if (!FloatNodes.TryGetValue((gameObject, property), out var node))
                return gameObjectActiveSelf;

            if (node.IsConstant)
            {
                var constValue = node.ConstantValue == 0;
                if (node.AppliedAlways || constValue == gameObjectActiveSelf)
                    return constValue;
            }

            return null;
        }

        public void Add(Component component, string prop, ComponentPropModNode<float> variableComponentPropModNode)
        {
            var key = (component, prop);
            if (!FloatNodes.TryGetValue(key, out var node))
                FloatNodes.Add(key, node = new RootPropModNode<float>());
            node.Add(variableComponentPropModNode);
        }
    }

    internal class ComponentNodeContainer : INodeContainer<ComponentPropModNode<float>>, INodeContainer
    {
        public Dictionary<(ComponentOrGameObject target, string prop), ComponentPropModNode<float>> FloatNodes { get; } =
            new Dictionary<(ComponentOrGameObject, string), ComponentPropModNode<float>>();

        IReadOnlyDictionary<(ComponentOrGameObject target, string prop), PropModNode<float>> INodeContainer.
            FloatNodes => Utils.CastDic<PropModNode<float>>().CastedDic(FloatNodes);

        public void Add(ComponentOrGameObject target, string prop, [NotNull] ComponentPropModNode<float> node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            FloatNodes.Add((target, prop), node);
        }
    }

    internal class ImmutableNodeContainer : INodeContainer<ImmutablePropModNode<float>>, INodeContainer
    {
        public Dictionary<(ComponentOrGameObject target, string prop), ImmutablePropModNode<float>> FloatNodes { get; } =
            new Dictionary<(ComponentOrGameObject, string), ImmutablePropModNode<float>>();

        IReadOnlyDictionary<(ComponentOrGameObject target, string prop), PropModNode<float>> INodeContainer.
            FloatNodes => Utils.CastDic<PropModNode<float>>().CastedDic(FloatNodes);

        public void Add(ComponentOrGameObject target, string prop, [NotNull] ImmutablePropModNode<float> node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            FloatNodes.Add((target, prop), node);
        }
    }

    internal sealed class RootPropModNode<T> : PropModNode<T>, IErrorContext
    {
        private readonly List<ComponentPropModNode<T>> _children = new List<ComponentPropModNode<T>>();

        public RootPropModNode(params RootPropModNode<T>[] props)
        {
            foreach (var prop in props)
            foreach (var child in prop._children)
                Add(child);
        }

        (bool, T) ComputeConstantInfo()
        {
            using (var enumerator = _children.GetEnumerator())
            {
                Debug.Assert(enumerator.MoveNext());

                if (!enumerator.Current.IsConstant) return (false, default);

                var value = enumerator.Current.ConstantValue;

                while (enumerator.MoveNext())
                {
                    if (!enumerator.Current.IsConstant) return (false, default);

                    if (!EqualityComparer<T>.Default.Equals(value, enumerator.Current.ConstantValue))
                        return (false, default);
                }

                return (true, value);
            }
        }

        public override bool AppliedAlways => _children.All(x => x.AppliedAlways);
        public override IEnumerable<ObjectReference> ContextReferences => _children.SelectMany(x => x.ContextReferences);
        public override bool IsConstant => ComputeConstantInfo().Item1;

        public override T ConstantValue
        {
            get
            {
                var info = ComputeConstantInfo();
                if (!info.Item1) throw new InvalidOperationException("Not Constant");
                return info.Item2;
            }
        }

        public IEnumerable<Component> SourceComponents => _children.Select(x => x.Component);

        public void Add(ComponentPropModNode<T> value)
        {
            _children.Add(value);
        }
    }

    internal abstract class ImmutablePropModNode<T> : PropModNode<T>
    {
    }

    internal class FloatAnimationCurveNode : ImmutablePropModNode<float>
    {
        public AnimationCurve Curve { get; }
        public AnimationClip Clip { get; }

        [CanBeNull]
        public static FloatAnimationCurveNode Create([NotNull] AnimationClip clip, EditorCurveBinding binding)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null) return null;
            if (curve.keys.Length == 0) return null;
            return new FloatAnimationCurveNode(clip, curve);
        }

        private FloatAnimationCurveNode(AnimationClip clip, AnimationCurve curve)
        {
            System.Diagnostics.Debug.Assert(curve.keys.Length > 0);
            Clip = clip;
            Curve = curve;
        }

        private object _parsedProperty;
        private object Parsed
        {
            get
            {
                if (_parsedProperty == null) return _parsedProperty = ParseProperty(Curve);
                return _parsedProperty;
            }
        }

        private static readonly object Variable = new object();

        public override bool IsConstant => Parsed is float;
        public override bool AppliedAlways => true;
        public override float ConstantValue => Parsed as float? ?? throw new InvalidOperationException("Non Constant");
        public override IEnumerable<ObjectReference> ContextReferences => new []{ ObjectRegistry.GetReference(Clip) };

        private static object ParseProperty(AnimationCurve curve)
        {
            if (curve.keys.Length == 1) return curve.keys[0].value;

            float constValue = 0;
            foreach (var (preKey, postKey) in curve.keys.ZipWithNext())
            {
                var preWeighted = preKey.weightedMode == WeightedMode.Out || preKey.weightedMode == WeightedMode.Both;
                var postWeighted = postKey.weightedMode == WeightedMode.In || postKey.weightedMode == WeightedMode.Both;

                if (preKey.value.CompareTo(postKey.value) != 0) return Variable;
                constValue = preKey.value;
                // it's constant
                if (float.IsInfinity(preKey.outWeight) || float.IsInfinity(postKey.inTangent)) continue;
                if (preKey.outTangent == 0 && postKey.inTangent == 0) continue;
                if (preWeighted && postWeighted && preKey.outWeight == 0 && postKey.inWeight == 0) continue;
                return Variable;
            }

            return constValue;
        }
    }

    internal class BlendTreeNode<T> : ImmutablePropModNode<T>
    {
        private readonly IEnumerable<ImmutablePropModNode<T>> _children;
        private readonly BlendTreeType _blendTreeType;

        public BlendTreeNode(IEnumerable<ImmutablePropModNode<T>> children, BlendTreeType blendTreeType, bool partial)
        {
            // expected to pass list or array
            // ReSharper disable once PossibleMultipleEnumeration
            Debug.Assert(children.Any());
            // ReSharper disable once PossibleMultipleEnumeration
            _children = children;
            _blendTreeType = blendTreeType;

            _appliedAlways = new Lazy<bool>(() =>
            {
                if (!WeightSumIsOne) return false;
                if (partial) return false;
                return _children.All(x => x.AppliedAlways);
            }, isThreadSafe: false);
            

            _constantInfo = new Lazy<(bool, T)>(() =>
            {
                if (!WeightSumIsOne) return (false, default);

                using (var enumerator = _children.GetEnumerator())
                {
                    Debug.Assert(enumerator.MoveNext());

                    if (!enumerator.Current.IsConstant) return (false, default);

                    var value = enumerator.Current.ConstantValue;

                    while (enumerator.MoveNext())
                    {
                        if (!enumerator.Current.IsConstant) return (false, default);

                        if (!EqualityComparer<T>.Default.Equals(value, enumerator.Current.ConstantValue))
                            return (false, default);
                    }

                    return (true, value);
                }
            }, isThreadSafe: false);
        }


        private bool WeightSumIsOne => _blendTreeType != BlendTreeType.Direct;
        private readonly Lazy<bool> _appliedAlways;
        public override bool AppliedAlways => _appliedAlways.Value;
        public override IEnumerable<ObjectReference> ContextReferences => _children.SelectMany(x => x.ContextReferences);

        private readonly Lazy<(bool, T)> _constantInfo;
        public override bool IsConstant => _constantInfo.Value.Item1;

        public override T ConstantValue
        {
            get
            {
                if (!_constantInfo.Value.Item1) throw new InvalidOperationException("Not Constant");
                return _constantInfo.Value.Item2;
            }
        }

    }

    abstract class ComponentPropModNode<T> : PropModNode<T>
    {
        protected ComponentPropModNode([NotNull] Component component)
        {
            if (!component) throw new ArgumentNullException(nameof(component));
            Component = component;
        }

        public Component Component { get; }

        public override IEnumerable<ObjectReference> ContextReferences => new [] { ObjectRegistry.GetReference(Component) };
    }

    class VariableComponentPropModNode<T> : ComponentPropModNode<T>
    {
        public VariableComponentPropModNode([NotNull] Component component) : base(component)
        {
        }

        public override T ConstantValue => throw new InvalidOperationException("Not Constant");
        public override bool IsConstant => false;
        public override bool AppliedAlways => false;
    }

    class AnimationComponentPropModNode<T> : ComponentPropModNode<T>
    {
        private readonly ImmutablePropModNode<T> _animation;

        public AnimationComponentPropModNode([NotNull] Component component, ImmutablePropModNode<T> animation) : base(component)
        {
            _animation = animation;
        }

        public override T ConstantValue => _animation.ConstantValue;
        public override bool IsConstant => _animation.IsConstant;
        public override bool AppliedAlways => false;
    }
}
