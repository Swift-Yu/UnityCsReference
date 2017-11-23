// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine;
using UnityEngine.Animations;
using Object = UnityEngine.Object;
using UnityEditorInternal;
using System.Collections.Generic;

namespace UnityEditor
{
    internal interface IConstraintStyle
    {
        GUIContent Activate { get; }
        GUIContent Zero { get; }

        GUIContent AtRest { get; }
        GUIContent Offset { get; }

        GUIContent Sources { get; }

        GUIContent Weight { get; }

        GUIContent IsActive { get; }
        GUIContent IsLocked { get; }

        GUIContent[] Axes { get; }

        GUIContent ConstraintSettings { get; }
    }

    internal abstract class ConstraintEditorBase : Editor
    {
        private bool m_ShowConstraintSettings = false;

        internal abstract SerializedProperty atRest { get; }
        internal abstract SerializedProperty offset { get; }
        internal abstract SerializedProperty weight { get; }
        internal abstract SerializedProperty isContraintActive { get; }
        internal abstract SerializedProperty isLocked { get; }
        internal abstract SerializedProperty sources { get; }

        private ReorderableList m_SourceList;

        private int m_SelectedSourceIdx = -1;
        protected int selectedSourceIndex { get { return m_SelectedSourceIdx; } set { m_SelectedSourceIdx = value; } }

        protected const int kSourceWeightWidth = 60;

        public void OnEnable(IConstraintStyle style)
        {
            Undo.undoRedoPerformed += OnUndoRedoPerformed;

            m_SourceList = new ReorderableList(serializedObject, sources, sources.editable, true, sources.editable, sources.editable);
            m_SourceList.drawElementCallback += DrawElementCallback;
            m_SourceList.onAddCallback += OnAddCallback;
            m_SourceList.drawHeaderCallback += rect => EditorGUI.LabelField(rect, style.Sources);
            m_SourceList.onRemoveCallback += OnRemoveCallback;
            m_SourceList.onSelectCallback += OnSelectedCallback;
            m_SourceList.elementHeightCallback += OnElementHeightCallback;

            if (sources.arraySize > 0 && m_SelectedSourceIdx == -1)
            {
                SelectSource(0);
            }
        }

        public void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        internal void OnUndoRedoPerformed()
        {
            // must call UserUpdateOffset to allow the offsets to be updated by the Undo system, otherwise the constraint can override them
            foreach (var t in targets)
                (t as IConstraintInternal).UserUpdateOffset();
        }

        protected void SelectSource(int index)
        {
            m_SelectedSourceIdx = index;

            if (m_SourceList.index != index)
            {
                m_SourceList.index = index;
            }
        }

        private void OnSelectedCallback(ReorderableList list)
        {
            SelectSource(list.index);
        }

        protected virtual void OnRemoveCallback(ReorderableList list)
        {
            ReorderableList.defaultBehaviours.DoRemoveButton(list);
            if (m_SelectedSourceIdx >= list.serializedProperty.arraySize)
            {
                SelectSource(list.serializedProperty.arraySize - 1);
            }
        }

        protected virtual void OnAddCallback(ReorderableList list)
        {
            var index = list.serializedProperty.arraySize;
            ReorderableList.defaultBehaviours.DoAddButton(list);

            var source = list.serializedProperty.GetArrayElementAtIndex(index);
            source.FindPropertyRelative("sourceTransform").objectReferenceValue = null;
            source.FindPropertyRelative("weight").floatValue = 1.0f;

            SelectSource(index);
        }

        protected virtual void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused)
        {
            rect.height = EditorGUIUtility.singleLineHeight;
            rect.y += 1;

            var element = sources.GetArrayElementAtIndex(index);
            var source = element.FindPropertyRelative("sourceTransform");
            var weight = element.FindPropertyRelative("weight");

            EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width - kSourceWeightWidth, EditorGUIUtility.singleLineHeight), source, GUIContent.none);
            EditorGUI.PropertyField(new Rect(rect.x + rect.width - kSourceWeightWidth, rect.y, kSourceWeightWidth, EditorGUIUtility.singleLineHeight), weight, GUIContent.none);
        }

        protected virtual float OnElementHeightCallback(int index)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        internal abstract void OnValueAtRestChanged();
        internal abstract void ShowFreezeAxesControl();

        /// Show the custom constraint properties that are not included in the foldout
        internal virtual void ShowCustomProperties() {}

        internal void ShowConstraintEditor<T>(IConstraintStyle style) where T : class, IConstraintInternal
        {
            if (m_SelectedSourceIdx == -1 || m_SelectedSourceIdx >= m_SourceList.serializedProperty.arraySize)
            {
                SelectSource(0);
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(style.Activate))
                    {
                        List<Object> recordObjects = new List<Object>();
                        recordObjects.AddRange(targets);
                        foreach (var t in targets)
                            recordObjects.Add((t as T).transform);
                        Undo.RegisterCompleteObjectUndo(recordObjects.ToArray(), "Activate the Constraint");

                        foreach (var t in targets)
                            (t as T).ActivateAndPreserveOffset();
                    }

                    if (GUILayout.Button(style.Zero))
                    {
                        List<Object> recordObjects = new List<Object>();
                        recordObjects.AddRange(targets);
                        foreach (var t in targets)
                            recordObjects.Add((t as T).transform);
                        Undo.RegisterCompleteObjectUndo(recordObjects.ToArray(), "Zero the Constraint");

                        foreach (var t in targets)
                            (t as T).ActivateWithZeroOffset();
                    }
                }
            }

            EditorGUILayout.PropertyField(isContraintActive, style.IsActive);
            EditorGUILayout.Slider(weight, 0.0f, 1.0f, style.Weight);
            ShowCustomProperties();

            m_ShowConstraintSettings = EditorGUILayout.Foldout(m_ShowConstraintSettings, style.ConstraintSettings, true);
            if (m_ShowConstraintSettings)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    EditorGUILayout.PropertyField(isLocked, style.IsLocked);
                }
                using (new EditorGUI.DisabledGroupScope(isLocked.boolValue))
                {
                    ShowValueAtRest(style);

                    ShowOffset<T>(style);
                }
                ShowFreezeAxesControl();
                EditorGUI.indentLevel--;
            }
            m_SourceList.DoLayoutList();
        }

        internal virtual void ShowValueAtRest(IConstraintStyle style)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(atRest, style.AtRest);
            if (EditorGUI.EndChangeCheck())
            {
                OnValueAtRestChanged();
            }
        }

        internal virtual void ShowOffset<T>(IConstraintStyle style) where T : class, IConstraintInternal
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(offset, style.Offset);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var t in targets)
                    (t as T).UserUpdateOffset();
            }
        }
    }
}