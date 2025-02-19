using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Anatawa12.AvatarOptimizer.AnimatorParsers
{
    interface IModificationsContainer
    {
        IReadOnlyDictionary<ComponentOrGameObject, IReadOnlyDictionary<string, AnimationFloatProperty>> ModifiedProperties { get; }
        ModificationsContainer ToMutable();
        ImmutableModificationsContainer ToImmutable();
    }

    static class ModificationsUtils
    {
        public static IReadOnlyDictionary<string, AnimationFloatProperty> GetModifiedProperties<T>(this T container,
            ComponentOrGameObject component)
            where T : IModificationsContainer
            => container.ModifiedProperties.TryGetValue(component, out var value)
                ? value
                : Utils.EmptyDictionary<string, AnimationFloatProperty>();

        public static bool? GetConstantValue<T>(this T container, ComponentOrGameObject obj, string property, bool currentValue)
            where T : IModificationsContainer
        {
            if (!container.GetModifiedProperties(obj).TryGetValue(property, out var prop))
                return currentValue;

            switch (prop.State)
            {
                case AnimationFloatProperty.PropertyState.ConstantAlways:
                    return FloatToBool(prop.ConstValue);
                case AnimationFloatProperty.PropertyState.ConstantPartially:
                    var constValue = FloatToBool(prop.ConstValue);
                    if (constValue == currentValue) return currentValue;
                    return null;
                case AnimationFloatProperty.PropertyState.Variable:
                    return null;
                case AnimationFloatProperty.PropertyState.Invalid:
                default:
                    throw new ArgumentOutOfRangeException();
            }

            bool? FloatToBool(float f)
            {
                switch (f)
                {
                    case 0:
                        return false;
                    case 1:
                        return true;
                    default:
                        return null;
                }
            }
        }

        public static IModificationsContainer MergeContainersSideBySide<T>([ItemNotNull] this IEnumerable<T> enumerable)
            where T : IModificationsContainer
        {
            using (var enumerator = enumerable.GetEnumerator())
            {
                if (!enumerator.MoveNext()) return ImmutableModificationsContainer.Empty;
                var first = enumerator.Current;
                if (!enumerator.MoveNext()) return first;

                // ReSharper disable once PossibleNullReferenceException // miss detections

                // merge all properties
                var merged = first.ToMutable();
                do merged.MergeAsSide(enumerator.Current);
                while (enumerator.MoveNext());

                return merged;
            }
        }
    }

    readonly struct ImmutableModificationsContainer : IModificationsContainer
    {
        private readonly IReadOnlyDictionary<ComponentOrGameObject, IReadOnlyDictionary<string, AnimationFloatProperty>> _modifiedProperties;

        public IReadOnlyDictionary<ComponentOrGameObject, IReadOnlyDictionary<string, AnimationFloatProperty>> ModifiedProperties =>
            _modifiedProperties ?? Utils.EmptyDictionary<ComponentOrGameObject, IReadOnlyDictionary<string, AnimationFloatProperty>>();
        public static ImmutableModificationsContainer Empty => default;

        public ImmutableModificationsContainer(ModificationsContainer from)
        {
            IReadOnlyDictionary<string, AnimationFloatProperty> MapDictionary(IReadOnlyDictionary<string, AnimationFloatProperty> dict) =>
                new ReadOnlyDictionary<string, AnimationFloatProperty>(dict.ToDictionary(p1 => p1.Key, p1 => p1.Value));

            _modifiedProperties =
                new ReadOnlyDictionary<ComponentOrGameObject, IReadOnlyDictionary<string, AnimationFloatProperty>>(from
                    .ModifiedProperties.ToDictionary(p => p.Key, p => MapDictionary(p.Value)));
        }

        public ModificationsContainer ToMutable() => new ModificationsContainer(this);
        public ImmutableModificationsContainer ToImmutable() => this;
    }

    class ModificationsContainer : IModificationsContainer
    {
        private readonly Dictionary<ComponentOrGameObject, Dictionary<string, AnimationFloatProperty>> _modifiedProperties;
        
        private static readonly IReadOnlyDictionary<string, AnimationFloatProperty> EmptyProperties =
            new ReadOnlyDictionary<string, AnimationFloatProperty>(new Dictionary<string, AnimationFloatProperty>());

        public IReadOnlyDictionary<ComponentOrGameObject, IReadOnlyDictionary<string, AnimationFloatProperty>> ModifiedProperties { get; }

        public ModificationsContainer()
        {
            _modifiedProperties = new Dictionary<ComponentOrGameObject, Dictionary<string, AnimationFloatProperty>>();
            ModifiedProperties = Utils.CastDic<IReadOnlyDictionary<string, AnimationFloatProperty>>()
                .CastedDic(_modifiedProperties);
        }

        public ModificationsContainer(ImmutableModificationsContainer from)
        {
            Dictionary<string, AnimationFloatProperty> MapDictionary(IReadOnlyDictionary<string, AnimationFloatProperty> dict) =>
                dict.ToDictionary(p1 => p1.Key, p1 => p1.Value);

            _modifiedProperties = from.ModifiedProperties
                .ToDictionary(p => p.Key, p => MapDictionary(p.Value));

            ModifiedProperties = Utils.CastDic<IReadOnlyDictionary<string, AnimationFloatProperty>>()
                .CastedDic(_modifiedProperties);
        }

        public ModificationsContainer ToMutable() => this;
        public ImmutableModificationsContainer ToImmutable() => new ImmutableModificationsContainer(this);

        #region Adding Modifications

        public ComponentAnimationUpdater ModifyObject(ComponentOrGameObject obj)
        {
            if (!_modifiedProperties.TryGetValue(obj, out var properties))
                _modifiedProperties.Add(obj, properties = new Dictionary<string, AnimationFloatProperty>());
            return new ComponentAnimationUpdater(properties);
        }

        public readonly struct ComponentAnimationUpdater
        {
            private readonly Dictionary<string, AnimationFloatProperty> _properties;

            public ComponentAnimationUpdater(Dictionary<string, AnimationFloatProperty> properties) => _properties = properties;

            public void AddModificationAsNewLayer(string propertyName, AnimationFloatProperty propertyState)
            {
                if (_properties.TryGetValue(propertyName, out var property))
                    _properties[propertyName] = property.Merge(propertyState, asNewLayer: true);
                else
                    _properties.Add(propertyName, propertyState);
            }
            
            public void AddModificationAsNewAdditiveLayer(string propertyName, AnimationFloatProperty propertyState)
            {
                switch (propertyState.State)
                {
                    case AnimationFloatProperty.PropertyState.ConstantAlways:
                    case AnimationFloatProperty.PropertyState.ConstantPartially:
                        // const 
                        break;
                    case AnimationFloatProperty.PropertyState.Variable:
                        if (_properties.TryGetValue(propertyName, out var property))
                            _properties[propertyName] = property.Merge(propertyState.Variable(), asNewLayer: false);
                        else
                            _properties.Add(propertyName, propertyState.Variable());
                        break;
                    case AnimationFloatProperty.PropertyState.Invalid:
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        #endregion

        /// <summary>
        /// Merge the specified Animator as new layer applied after this layer
        /// </summary>
        public void MergeAsNewLayer<T>(T parsed, AnimatorWeightState weightState) where T : IModificationsContainer
        {
            if (weightState == AnimatorWeightState.AlwaysZero) return;
            foreach (var (obj, properties) in parsed.ModifiedProperties)
            {
                var updater = ModifyObject(obj);

                foreach (var (propertyName, propertyState) in properties)
                {
                    updater.AddModificationAsNewLayer(propertyName, weightState.ApplyToProperty(propertyState));
                }
            }
        }

        /// <summary>
        /// Merge the specified Animator as new layer applied after this layer
        /// </summary>
        public void MergeAsNewAdditiveLayer<T>(T parsed, AnimatorWeightState weightState) where T : IModificationsContainer
        {
            if (weightState == AnimatorWeightState.AlwaysZero) return;
            foreach (var (obj, properties) in parsed.ModifiedProperties)
            {
                var updater = ModifyObject(obj);

                foreach (var (propertyName, propertyState) in properties)
                    updater.AddModificationAsNewAdditiveLayer(propertyName, weightState.ApplyToProperty(propertyState));
            }
        }

        public void MergeAsSide<T>(T other) where T : IModificationsContainer
        {
            foreach (var (obj, (thisProperties, otherProperties)) in _modifiedProperties.ZipByKey(other.ModifiedProperties))
            {
                if (otherProperties == null)
                {
                    // the object is modified by current only: everything in thisProperties should be marked partially
                    foreach (var property in thisProperties.Keys.ToArray())
                        thisProperties[property] = thisProperties[property].PartiallyApplied();
                }
                else if (thisProperties == null)
                {
                    // the object is modified by other only: copy otherProperties with everything marked partially
                    _modifiedProperties.Add(obj, EverythingPartially(otherProperties));
                }
                else
                {
                    // the object id modified by both

                    foreach (var (property, (thisState, otherState)) in thisProperties.ZipByKey(otherProperties))
                    {
                        if (otherState.State == AnimationFloatProperty.PropertyState.Invalid)
                        {
                            // the property is modified by current only: this modification should be marked partially
                            thisProperties[property] = thisState.PartiallyApplied();
                        }
                        else if (thisState.State == AnimationFloatProperty.PropertyState.Invalid)
                        {
                            // the property is modified by other only: copied with marked partially
                            thisProperties.Add(property, otherState.PartiallyApplied());
                        }
                        else
                        {
                            // the property is modified by both: merge the property modification
                            thisProperties[property] = thisState.Merge(otherState, asNewLayer: false);
                        }
                    }
                }
            }

            Dictionary<string, AnimationFloatProperty>
                EverythingPartially(IReadOnlyDictionary<string, AnimationFloatProperty> dictionary) =>
                dictionary.ToDictionary(k => k.Key, v => v.Value.PartiallyApplied());
        }

        // make all property modification variable
        public void MakeAllVariable()
        {
            foreach (var properties in _modifiedProperties.Values)
                foreach (var name in properties.Keys.ToArray())
                    properties[name] = properties[name].Variable();
        }
    }

    readonly struct AnimationFloatProperty : IEquatable<AnimationFloatProperty>
    {
        public readonly PropertyState State;
        private readonly float _constValue;

        public float ConstValue
        {
            get
            {
                switch (State)
                {
                    case PropertyState.ConstantAlways:
                    case PropertyState.ConstantPartially:
                        return _constValue;
                    case PropertyState.Invalid:
                    case PropertyState.Variable:
                        throw new InvalidOperationException("Non Const State");
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public bool IsConst
        {
            get
            {
                switch (State)
                {
                    case PropertyState.ConstantAlways:
                    case PropertyState.ConstantPartially:
                        return true;
                    case PropertyState.Invalid:
                    case PropertyState.Variable:
                        return false;
                    default:
                        throw new ArgumentOutOfRangeException();
                } 
            }
        }

        private readonly IModificationSource[] _sources;
        public ReadOnlySpan<IModificationSource> Sources => _sources ?? Array.Empty<IModificationSource>();

        private AnimationFloatProperty(PropertyState state, float constValue, params IModificationSource[] modifiers) =>
            (State, _constValue, _sources) = (state, constValue, modifiers);

        public static AnimationFloatProperty ConstAlways(float value, [NotNull] IModificationSource modifier)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            return ConstAlways0(value, new[] { modifier });
        }

        public static AnimationFloatProperty ConstPartially(float value, [NotNull] IModificationSource modifier)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            return ConstPartially0(value, new[] { modifier });
        }

        public static AnimationFloatProperty Variable([NotNull] IModificationSource modifier)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            return Variable0(new[] { modifier });
        }

        private static AnimationFloatProperty ConstAlways0(float value, IModificationSource[] modifiers) =>
            new AnimationFloatProperty(PropertyState.ConstantAlways, value, modifiers);

        private static AnimationFloatProperty ConstPartially0(float value, IModificationSource[] modifiers) =>
            new AnimationFloatProperty(PropertyState.ConstantPartially, value, modifiers);

        private static AnimationFloatProperty Variable0(IModificationSource[] modifiers) =>
            new AnimationFloatProperty(PropertyState.Variable, float.NaN, modifiers);

        private IModificationSource[] MergeSource(IModificationSource[] aSource, IModificationSource[] bSource)
        {
            if (aSource == null) return bSource;
            if (bSource == null) return aSource;

            return aSource.Concat(bSource).Distinct().ToArray();
        }

        public AnimationFloatProperty Merge(AnimationFloatProperty b, bool asNewLayer)
        {
            // if asNewLayer and new layer is constant always, the value is used
            if (asNewLayer && b.State == PropertyState.ConstantAlways) return b;

            if (State == PropertyState.Variable) return Variable0(MergeSource(_sources, b._sources));
            if (b.State == PropertyState.Variable) return Variable0(MergeSource(_sources, b._sources));

            // now they are constant.
            if (ConstValue.CompareTo(b.ConstValue) != 0) return Variable0(MergeSource(_sources, b._sources));

            var value = ConstValue;

            if (State == PropertyState.ConstantPartially) return ConstPartially0(value, MergeSource(_sources, b._sources));
            if (b.State == PropertyState.ConstantPartially) return ConstPartially0(value, MergeSource(_sources, b._sources));

            System.Diagnostics.Debug.Assert(State == PropertyState.ConstantAlways);
            System.Diagnostics.Debug.Assert(b.State == PropertyState.ConstantAlways);

            return this;
        }

        public static AnimationFloatProperty? ParseProperty(AnimationCurve curve, IModificationSource source)
        {
            if (curve.keys.Length == 0) return null;
            if (curve.keys.Length == 1)
                return ConstAlways(curve.keys[0].value, source);

            float constValue = 0;
            foreach (var (preKey, postKey) in curve.keys.ZipWithNext())
            {
                var preWeighted = preKey.weightedMode == WeightedMode.Out || preKey.weightedMode == WeightedMode.Both;
                var postWeighted = postKey.weightedMode == WeightedMode.In || postKey.weightedMode == WeightedMode.Both;

                if (preKey.value.CompareTo(postKey.value) != 0) return Variable(source);
                constValue = preKey.value;
                // it's constant
                if (float.IsInfinity(preKey.outWeight) || float.IsInfinity(postKey.inTangent))
                    continue;
                if (preKey.outTangent == 0 && postKey.inTangent == 0)
                    continue;
                if (preWeighted && postWeighted && preKey.outWeight == 0 && postKey.inWeight == 0)
                    continue;
                return Variable(source);
            }

            return ConstAlways(constValue, source);
        }

        public AnimationFloatProperty PartiallyApplied()
        {
            switch (State)
            {
                case PropertyState.ConstantAlways:
                    return ConstPartially0(ConstValue, _sources);
                case PropertyState.ConstantPartially:
                case PropertyState.Variable:
                    return this;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public AnimationFloatProperty Variable() => Variable0(_sources);

        public enum PropertyState
        {
            Invalid = 0,
            ConstantAlways,
            ConstantPartially,
            Variable,
        }

        public bool Equals(AnimationFloatProperty other)
        {
            switch (State)
            {
                case PropertyState.ConstantAlways:
                case PropertyState.ConstantPartially:
                    return State == other.State && _constValue.Equals(other.ConstValue);
                case PropertyState.Invalid:
                case PropertyState.Variable:
                default:
                    return State == other.State;
            }
        }

        public override bool Equals(object obj)
        {
            return obj is AnimationFloatProperty other && Equals(other);
        }

        public override int GetHashCode()
        {
            switch (State)
            {
                case PropertyState.ConstantAlways:
                case PropertyState.ConstantPartially:
                    return unchecked(((int)State * 397) ^ _constValue.GetHashCode());
                default:
                case PropertyState.Invalid:
                case PropertyState.Variable:
                    return unchecked(((int)State * 397));
            }
        }

        public override string ToString()
        {
            switch (State)
            {
                case PropertyState.Invalid:
                    return "Invalid";
                case PropertyState.ConstantAlways:
                    return $"ConstantAlways({ConstValue})";
                case PropertyState.ConstantPartially:
                    return $"ConstantPartially({ConstValue})";
                case PropertyState.Variable:
                    return "Variable";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
    
    enum AnimatorWeightState
    {
        NotChanged,
        AlwaysZero,
        AlwaysOne,
        EitherZeroOrOne,
        Variable
    }

    class AnimatorLayerWeightMap<TKey>
    {
        private Dictionary<TKey, AnimatorWeightState> _backed =
            new Dictionary<TKey, AnimatorWeightState>();

        public AnimatorWeightState this[TKey key]
        {
            get
            {
                _backed.TryGetValue(key, out var state);
                return state;
            }
            set => _backed[key] = value;
        }

        public AnimatorWeightState Get(TKey key) => this[key];
    }

    static class AnimatorLayerWeightStates
    {
        public static AnimatorWeightState WeightStateFor(float duration, float weight) =>
            duration != 0 ? AnimatorWeightState.Variable : WeightStateFor(weight);

        public static AnimatorWeightState WeightStateFor(float weight)
        {
            switch (weight)
            {
                case 0:
                    return AnimatorWeightState.AlwaysZero;
                case 1:
                    return AnimatorWeightState.AlwaysOne;
                default:
                    return AnimatorWeightState.Variable;
            }
        }
        
        public static AnimatorWeightState Merge(this AnimatorWeightState a, AnimatorWeightState b)
        {
            // 25 pattern
            if (a == b) return a;

            if (a == AnimatorWeightState.NotChanged) return b;
            if (b == AnimatorWeightState.NotChanged) return a;

            if (a == AnimatorWeightState.Variable) return AnimatorWeightState.Variable;
            if (b == AnimatorWeightState.Variable) return AnimatorWeightState.Variable;

            if (a == AnimatorWeightState.AlwaysOne && b == AnimatorWeightState.AlwaysZero)
                return AnimatorWeightState.EitherZeroOrOne;
            if (b == AnimatorWeightState.AlwaysOne && a == AnimatorWeightState.AlwaysZero)
                return AnimatorWeightState.EitherZeroOrOne;

            if (a == AnimatorWeightState.EitherZeroOrOne && b == AnimatorWeightState.AlwaysZero)
                return AnimatorWeightState.EitherZeroOrOne;
            if (b == AnimatorWeightState.EitherZeroOrOne && a == AnimatorWeightState.AlwaysZero)
                return AnimatorWeightState.EitherZeroOrOne;

            if (a == AnimatorWeightState.EitherZeroOrOne && b == AnimatorWeightState.AlwaysOne)
                return AnimatorWeightState.EitherZeroOrOne;
            if (b == AnimatorWeightState.EitherZeroOrOne && a == AnimatorWeightState.AlwaysOne)
                return AnimatorWeightState.EitherZeroOrOne;

            throw new ArgumentOutOfRangeException();
        }

        public static AnimationFloatProperty ApplyToProperty(this AnimatorWeightState state, AnimationFloatProperty property)
        {
            switch (state)
            {
                case AnimatorWeightState.AlwaysOne:
                    return property;
                case AnimatorWeightState.EitherZeroOrOne:
                    return property.PartiallyApplied();
                case AnimatorWeightState.Variable:
                    return property.Variable();
                case AnimatorWeightState.NotChanged:
                case AnimatorWeightState.AlwaysZero:
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        public static AnimatorWeightState ForAlwaysApplied(bool alwaysApplied) =>
            alwaysApplied ? AnimatorWeightState.AlwaysOne : AnimatorWeightState.EitherZeroOrOne;
    }
}