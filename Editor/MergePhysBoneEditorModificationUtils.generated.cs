#if AAO_VRCSDK3_AVATARS

// // <generated />
// generated by .MergePhysBoneEditorModificationUtils.ts
using System.Collections.Generic;
using UnityEditor;
using JetBrains.Annotations;

namespace Anatawa12.AvatarOptimizer
{
    partial class MergePhysBoneEditorModificationUtils
    {
        protected partial class CurveConfigProp : OverridePropBase
        {
            public readonly SerializedProperty OverrideValue;
            public SerializedProperty SourceValue { get; private set; }
            public readonly string PhysBoneValueName;
            public readonly SerializedProperty OverrideCurve;
            public SerializedProperty SourceCurve { get; private set; }
            public readonly string PhysBoneCurveName;

            public CurveConfigProp(
                [NotNull] SerializedProperty rootProperty
                , [NotNull] string physBoneValueName
                , [NotNull] string physBoneCurveName
                ) : base(rootProperty)
            {
                OverrideValue = rootProperty.FindPropertyRelative("value");
                PhysBoneValueName = physBoneValueName;
                OverrideCurve = rootProperty.FindPropertyRelative("curve");
                PhysBoneCurveName = physBoneCurveName;
            }

            internal override void UpdateSource(SerializedObject sourcePb)
            {
                SourceValue = sourcePb.FindProperty(PhysBoneValueName);
                SourceCurve = sourcePb.FindProperty(PhysBoneCurveName);
            }
            public SerializedProperty GetValueProperty(bool @override) => @override ? OverrideValue : SourceValue;
            public SerializedProperty GetCurveProperty(bool @override) => @override ? OverrideCurve : SourceCurve;
            public override IEnumerable<(string, SerializedProperty)> GetActiveProps(bool @override) {
                if (@override)
                    return new[] {
                        (PhysBoneValueName, OverrideValue),
                        (PhysBoneCurveName, OverrideCurve),
                    };
                else
                    return new[] {
                        (PhysBoneValueName, SourceValue),
                        (PhysBoneCurveName, SourceCurve),
                    };
            }
        }
        protected partial class CurveVector3ConfigProp : OverridePropBase
        {
            public readonly SerializedProperty OverrideValue;
            public SerializedProperty SourceValue { get; private set; }
            public readonly string PhysBoneValueName;
            public readonly SerializedProperty OverrideCurveX;
            public SerializedProperty SourceCurveX { get; private set; }
            public readonly string PhysBoneCurveXName;
            public readonly SerializedProperty OverrideCurveY;
            public SerializedProperty SourceCurveY { get; private set; }
            public readonly string PhysBoneCurveYName;
            public readonly SerializedProperty OverrideCurveZ;
            public SerializedProperty SourceCurveZ { get; private set; }
            public readonly string PhysBoneCurveZName;

            public CurveVector3ConfigProp(
                [NotNull] SerializedProperty rootProperty
                , [NotNull] string physBoneValueName
                , [NotNull] string physBoneCurveXName
                , [NotNull] string physBoneCurveYName
                , [NotNull] string physBoneCurveZName
                ) : base(rootProperty)
            {
                OverrideValue = rootProperty.FindPropertyRelative("value");
                PhysBoneValueName = physBoneValueName;
                OverrideCurveX = rootProperty.FindPropertyRelative("curveX");
                PhysBoneCurveXName = physBoneCurveXName;
                OverrideCurveY = rootProperty.FindPropertyRelative("curveY");
                PhysBoneCurveYName = physBoneCurveYName;
                OverrideCurveZ = rootProperty.FindPropertyRelative("curveZ");
                PhysBoneCurveZName = physBoneCurveZName;
            }

            internal override void UpdateSource(SerializedObject sourcePb)
            {
                SourceValue = sourcePb.FindProperty(PhysBoneValueName);
                SourceCurveX = sourcePb.FindProperty(PhysBoneCurveXName);
                SourceCurveY = sourcePb.FindProperty(PhysBoneCurveYName);
                SourceCurveZ = sourcePb.FindProperty(PhysBoneCurveZName);
            }
            public SerializedProperty GetValueProperty(bool @override) => @override ? OverrideValue : SourceValue;
            public SerializedProperty GetCurveXProperty(bool @override) => @override ? OverrideCurveX : SourceCurveX;
            public SerializedProperty GetCurveYProperty(bool @override) => @override ? OverrideCurveY : SourceCurveY;
            public SerializedProperty GetCurveZProperty(bool @override) => @override ? OverrideCurveZ : SourceCurveZ;
            public override IEnumerable<(string, SerializedProperty)> GetActiveProps(bool @override) {
                if (@override)
                    return new[] {
                        (PhysBoneValueName, OverrideValue),
                        (PhysBoneCurveXName, OverrideCurveX),
                        (PhysBoneCurveYName, OverrideCurveY),
                        (PhysBoneCurveZName, OverrideCurveZ),
                    };
                else
                    return new[] {
                        (PhysBoneValueName, SourceValue),
                        (PhysBoneCurveXName, SourceCurveX),
                        (PhysBoneCurveYName, SourceCurveY),
                        (PhysBoneCurveZName, SourceCurveZ),
                    };
            }
        }
        protected partial class PermissionConfigProp : OverridePropBase
        {
            public readonly SerializedProperty OverrideValue;
            public SerializedProperty SourceValue { get; private set; }
            public readonly string PhysBoneValueName;
            public readonly SerializedProperty OverrideFilter;
            public SerializedProperty SourceFilter { get; private set; }
            public readonly string PhysBoneFilterName;

            public PermissionConfigProp(
                [NotNull] SerializedProperty rootProperty
                , [NotNull] string physBoneValueName
                , [NotNull] string physBoneFilterName
                ) : base(rootProperty)
            {
                OverrideValue = rootProperty.FindPropertyRelative("value");
                PhysBoneValueName = physBoneValueName;
                OverrideFilter = rootProperty.FindPropertyRelative("filter");
                PhysBoneFilterName = physBoneFilterName;
            }

            internal override void UpdateSource(SerializedObject sourcePb)
            {
                SourceValue = sourcePb.FindProperty(PhysBoneValueName);
                SourceFilter = sourcePb.FindProperty(PhysBoneFilterName);
            }
            public SerializedProperty GetValueProperty(bool @override) => @override ? OverrideValue : SourceValue;
            public SerializedProperty GetFilterProperty(bool @override) => @override ? OverrideFilter : SourceFilter;
        }
        protected partial class ValueConfigProp : OverridePropBase
        {
            public readonly SerializedProperty OverrideValue;
            public SerializedProperty SourceValue { get; private set; }
            public readonly string PhysBoneValueName;

            public ValueConfigProp(
                [NotNull] SerializedProperty rootProperty
                , [NotNull] string physBoneValueName
                ) : base(rootProperty)
            {
                OverrideValue = rootProperty.FindPropertyRelative("value");
                PhysBoneValueName = physBoneValueName;
            }

            internal override void UpdateSource(SerializedObject sourcePb)
            {
                SourceValue = sourcePb.FindProperty(PhysBoneValueName);
            }
            public SerializedProperty GetValueProperty(bool @override) => @override ? OverrideValue : SourceValue;
            public override IEnumerable<(string, SerializedProperty)> GetActiveProps(bool @override) {
                if (@override)
                    return new[] {
                        (PhysBoneValueName, OverrideValue),
                    };
                else
                    return new[] {
                        (PhysBoneValueName, SourceValue),
                    };
            }
        }
        protected partial class NoOverrideValueConfigProp : PropBase
        {
            public readonly SerializedProperty OverrideValue;
            public SerializedProperty SourceValue { get; private set; }
            public readonly string PhysBoneValueName;

            public NoOverrideValueConfigProp(
                [NotNull] SerializedProperty rootProperty
                , [NotNull] string physBoneValueName
                ) : base(rootProperty)
            {
                OverrideValue = rootProperty.FindPropertyRelative("value");
                PhysBoneValueName = physBoneValueName;
            }

            internal override void UpdateSource(SerializedObject sourcePb)
            {
                SourceValue = sourcePb.FindProperty(PhysBoneValueName);
            }
            public SerializedProperty GetValueProperty(bool @override) => @override ? OverrideValue : SourceValue;
        }
    }
}

#endif
