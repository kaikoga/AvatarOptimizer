using CustomLocalization4EditorExtension;
using JetBrains.Annotations;
using UnityEditor;

namespace Anatawa12.AvatarOptimizer
{
    abstract class AvatarTagComponentEditorBase : Editor
    {
        public sealed override void OnInspectorGUI()
        {
            CL4EE.DrawLanguagePicker();

            var description = Description;
            if (!string.IsNullOrEmpty(description))
                EditorGUILayout.HelpBox(description, MessageType.None);

            OnInspectorGUIInner();
        }

        private string _descriptionKey;
        [CanBeNull]
        protected virtual string Description
        {
            get
            {
                if (_descriptionKey == null)
                    _descriptionKey = $"{serializedObject.targetObject.GetType().Name}:description";
                return CL4EE.GetLocalization()?.TryTr(_descriptionKey);
            }
        }

        protected abstract void OnInspectorGUIInner();
    }

    [CustomEditor(typeof(AvatarTagComponent), true)]
    class DefaultAvatarTagComponentEditor : AvatarTagComponentEditorBase
    {
        protected override void OnInspectorGUIInner()
        {
            serializedObject.UpdateIfRequiredOrScript();
            var iterator = serializedObject.GetIterator();

            var enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                if ("m_Script" != iterator.propertyPath)
                    EditorGUILayout.PropertyField(iterator, true);
                enterChildren = false;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
    
    internal abstract class UnsupportedAvatarTagComponentEditor : DefaultAvatarTagComponentEditor
    {
        protected override void OnInspectorGUIInner()
        {
            EditorGUILayout.HelpBox(CL4EE.GetLocalization()?.TryTr("Unsupported"), MessageType.Error);
            base.OnInspectorGUIInner();
        }
    }

}
