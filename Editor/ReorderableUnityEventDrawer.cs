namespace Games.NoSoySauce.DeveloperTools.ReorderableUnityEvents
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using Components;
    using UnityEditor;
    using UnityEditor.Callbacks;
    using UnityEditorInternal;
    using UnityEngine;
    using UnityEngine.Events;
    using UnityEngine.EventSystems;
    using UnityEngine.SceneManagement;
    using Object = UnityEngine.Object;
    using Random = UnityEngine.Random;

    /// <summary>
    ///     Displays a custom reorderable inspector collection in a collapsible drawer.
    /// </summary>
    [CustomPropertyDrawer(typeof(UnityEventBase), true), 
     CustomPropertyDrawer(typeof(UnityEvent), true), 
     CustomPropertyDrawer(typeof(UnityEvent<>), true),
     CustomPropertyDrawer(typeof(UnityEvent<BaseEventData>), true)]
    public class ReorderableUnityEventDrawer : PropertyDrawer
    {
        #region Data Types

        /// <summary>
        ///     Container for data related to a listener function.
        /// </summary>
        protected class FunctionData
        {
            public FunctionData(SerializedProperty listener, Object target = null, MethodInfo method = null, PersistentListenerMode mode = PersistentListenerMode.EventDefined)
            {
                listenerElement = listener;
                targetObject = target;
                targetMethod = method;
                listenerMode = mode;
            }

            public SerializedProperty listenerElement;
            public Object targetObject;
            public MethodInfo targetMethod;
            public PersistentListenerMode listenerMode;
        }

        /// <summary>
        ///     Container for storing current state of <see cref="ReorderableUnityEventDrawer" />.
        /// </summary>
        protected class DrawerState
        {
            public ReorderableList reorderableList;
            public int lastSelectedIndex;

            // Invoke field tracking
            public string currentInvokeStrArg;
            public int currentInvokeIntArg;
            public float currentInvokeFloatArg;
            public bool currentInvokeBoolArg;
            public Object currentInvokeObjectArg;
        }

        /// <summary>
        ///     TODO: docs
        /// </summary>
        private class ComponentTypeCount
        {
            public int totalCount;
            public int currentCount = 1;
        }

        #endregion

        #region Static Fields

        private static ReorderableUnityEventHandler.ReorderableUnityEventSettings CachedSettings;
        
        private static Color FocusedColor = new Color(0.172549f, 0.3647059f, 0.5294118f);

        #endregion

        #region Static Methods (UnityEvent Logic)

        /// <summary>
        ///     Invokes provided method (<see cref="MethodInfo" />).
        /// </summary>
        /// <remarks>
        ///     This invoke call respects "Runtime Only"/"Editor and Runtime"/"Off" setting on the <see cref="UnityEvent" />
        ///     listener which is being invoked.
        /// </remarks>
        /// <param name="method"><see cref="MethodInfo" /> of the method to invoke.</param>
        /// <param name="targets">Objects to invoke method on (<see cref="UnityEvent" /> listeners).</param>
        /// <param name="argValue">Value of the argument provided to invoke call.</param>
        private static void InvokeOnTargetEvents(MethodInfo method, IEnumerable<object> targets, object argValue)
        {
            foreach (object target in targets)
            {
                if (argValue != null)
                    method.Invoke(target, new[] {argValue});
                else
                    method.Invoke(target, new object[] { });
            }
        }

        /// <summary>
        ///     Where the event data actually gets added when you choose a function.
        ///     TODO: adequate docs
        /// </summary>
        /// <param name="functionUserData">TODO: docs</param>
        protected static void SetEventFunctionCallback(object functionUserData)
        {
            var functionData = (FunctionData)functionUserData;

            SerializedProperty serializedElement = functionData.listenerElement;

            SerializedProperty serializedTarget = serializedElement.FindPropertyRelative("m_Target");
            SerializedProperty serializedMethodName = serializedElement.FindPropertyRelative("m_MethodName");
            SerializedProperty serializedArgs = serializedElement.FindPropertyRelative("m_Arguments");
            SerializedProperty serializedMode = serializedElement.FindPropertyRelative("m_Mode");

            SerializedProperty serializedArgAssembly = serializedArgs.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName");
            SerializedProperty serializedArgObjectValue = serializedArgs.FindPropertyRelative("m_ObjectArgument");

            serializedTarget.objectReferenceValue = functionData.targetObject;
            serializedMethodName.stringValue = functionData.targetMethod.Name;
            serializedMode.enumValueIndex = (int) functionData.listenerMode;

            if (functionData.listenerMode == PersistentListenerMode.Object)
            {
                ParameterInfo[] methodParams = functionData.targetMethod.GetParameters();
                if (methodParams.Length == 1 && typeof(Object).IsAssignableFrom(methodParams[0].ParameterType))
                    serializedArgAssembly.stringValue = methodParams[0].ParameterType.AssemblyQualifiedName;
                else
                    serializedArgAssembly.stringValue = typeof(Object).AssemblyQualifiedName;
            }
            else
            {
                serializedArgAssembly.stringValue = typeof(Object).AssemblyQualifiedName;
                serializedArgObjectValue.objectReferenceValue = null;
            }

            Type argType = ReorderableUnityEventHandler.FindTypeInAllAssemblies(serializedArgAssembly.stringValue);
            if (!typeof(Object).IsAssignableFrom(argType) || !argType.IsInstanceOfType(serializedArgObjectValue.objectReferenceValue))
                serializedArgObjectValue.objectReferenceValue = null;

            functionData.listenerElement.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        ///     TODO: docs
        /// </summary>
        /// <param name="functionUserData">TODO: docs</param>
        protected static void ClearEventFunctionCallback(object functionUserData)
        {
            var functionData = (FunctionData)functionUserData;

            functionData.listenerElement.FindPropertyRelative("m_Mode").enumValueIndex = (int) PersistentListenerMode.Void;
            functionData.listenerElement.FindPropertyRelative("m_MethodName").stringValue = null;
            functionData.listenerElement.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        ///     Resets the state of the given <see cref="UnityEvent" /> listener.
        /// </summary>
        /// <param name="serialiedListener">Listener to reset the state of.</param>
        protected virtual void ResetEventState(SerializedProperty serialiedListener)
        {
            SerializedProperty serializedCallState = serialiedListener.FindPropertyRelative("m_CallState");
            SerializedProperty serializedTarget = serialiedListener.FindPropertyRelative("m_Target");
            SerializedProperty serializedMethodName = serialiedListener.FindPropertyRelative("m_MethodName");
            SerializedProperty serializedMode = serialiedListener.FindPropertyRelative("m_Mode");
            SerializedProperty serializedArgs = serialiedListener.FindPropertyRelative("m_Arguments");

            serializedCallState.enumValueIndex = (int) UnityEventCallState.RuntimeOnly;
            serializedTarget.objectReferenceValue = null;
            serializedMethodName.stringValue = null;
            serializedMode.enumValueIndex = (int) PersistentListenerMode.Void;

            serializedArgs.FindPropertyRelative("m_IntArgument").intValue = 0;
            serializedArgs.FindPropertyRelative("m_FloatArgument").floatValue = 0f;
            serializedArgs.FindPropertyRelative("m_BoolArgument").boolValue = false;
            serializedArgs.FindPropertyRelative("m_StringArgument").stringValue = null;
            serializedArgs.FindPropertyRelative("m_ObjectArgument").objectReferenceValue = null;
            serializedArgs.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = null;
        }

        #endregion

        #region Static Methods (Utility)

        /// <summary>
        ///     Returns <see cref="Rect" />s used for drawing a single listener of <see cref="UnityEvent" />.
        /// </summary>
        /// <param name="rect">Initial rect. TODO: docs</param>
        /// <returns>
        ///     An array of 4 <see cref="Rect" />s in the following order: enabled field, GameObject field, function field and
        ///     argument field.
        /// </returns>
        protected static Rect[] GetEventListenerRects(Rect rect)
        {
            var rects = new Rect[4];

            rect.height = EditorGUIUtility.singleLineHeight;
            rect.y += 2;

            // enabled field
            rects[0] = rect;
            rects[0].width *= 0.3f;

            // game object field
            rects[1] = rects[0];
            rects[1].x += 1;
            rects[1].width -= 2;
            rects[1].y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            // function field
            rects[2] = rect;
            rects[2].xMin = rects[1].xMax + 5;

            // argument field
            rects[3] = rects[2];
            rects[3].y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            return rects;
        }

#if UNITY_2018_4_OR_NEWER
        /// <summary>
        ///     TODO: docs
        /// </summary>
        /// <param name="property">TODO: docs</param>
        /// <returns>TODO: docs</returns>
        private static UnityEventBase GetDummyEvent(SerializedProperty property)
        {
            Object targetObject = property.serializedObject.targetObject;
            if (targetObject == null)
                return new UnityEvent();

            UnityEventBase dummyEvent = null;
            Type targetType = targetObject.GetType();
            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            do
            {
                dummyEvent = GetDummyEventStep(property.propertyPath, targetType, bindingFlags);
                bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic;
                targetType = targetType.BaseType;
            } while (dummyEvent == null && targetType != null);

            return dummyEvent ?? new UnityEvent();
        }

        /// <summary>
        ///     TODO: docs
        /// </summary>
        /// <param name="propertyPath">TODO: docs</param>
        /// <param name="propertyType">TODO: docs</param>
        /// <param name="bindingFlags">TODO: docs</param>
        /// <returns>TODO: docs</returns>
        private static UnityEventBase GetDummyEventStep(string propertyPath, Type propertyType, BindingFlags bindingFlags)
        {
            UnityEventBase dummyEvent = null;

            while (propertyPath.Length > 0)
            {
                if (propertyPath.StartsWith("."))
                    propertyPath = propertyPath.Substring(1);

                string[] splitPath = propertyPath.Split(new[] {'.'}, 2);

                FieldInfo newField = propertyType.GetField(splitPath[0], bindingFlags);

                if (newField == null)
                    break;

                propertyType = newField.FieldType;
                if (propertyType.IsArray)
                    propertyType = propertyType.GetElementType();
                else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>)) propertyType = propertyType.GetGenericArguments()[0];

                if (splitPath.Length == 1)
                    break;

                propertyPath = splitPath[1];
                if (propertyPath.StartsWith("Array.data["))
                    propertyPath = propertyPath.Split(new[] {']'}, 2)[1];
            }

            if (propertyType.IsSubclassOf(typeof(UnityEventBase)))
                dummyEvent = Activator.CreateInstance(propertyType) as UnityEventBase;

            return dummyEvent;
        }
#endif

        /// <summary>
        ///     Gets object type corresponding to the given <see cref="PersistentListenerMode" />.
        /// </summary>
        /// <param name="listenerMode">Listener mode to get the type for.</param>
        /// <returns><see cref="Type" /> corresponding to the provided <see cref="PersistentListenerMode" />.</returns>
        protected static Type[] GetTypeForListenerMode(PersistentListenerMode listenerMode)
        {
            switch (listenerMode)
            {
                case PersistentListenerMode.EventDefined:
                case PersistentListenerMode.Void:
                    return new Type[] { };
                case PersistentListenerMode.Object:
                    return new[] {typeof(Object)};
                case PersistentListenerMode.Int:
                    return new[] {typeof(int)};
                case PersistentListenerMode.Float:
                    return new[] {typeof(float)};
                case PersistentListenerMode.String:
                    return new[] {typeof(string)};
                case PersistentListenerMode.Bool:
                    return new[] {typeof(bool)};
            }

            return new Type[] { };
        }

        /// <summary>
        ///     Finds all methods on a given Object which match the given PersistentListenerMode.
        /// </summary>
        /// <remarks>
        ///     Values provided by this method are used to populate a dropdown list of functions displayed when selecting a
        ///     function in UnityEvent listener Inspector.
        /// </remarks>
        /// <param name="targetObject">Object to get methods from.</param>
        /// <param name="listenerMode">Mode of UnityEvent listener.</param>
        /// <param name="methodInfos">This list will be populated with <see cref="FunctionData" /> for each valid method found.</param>
        /// <param name="customArgTypes">TODO: docs</param>
        protected static void FindValidMethods(Object targetObject, PersistentListenerMode listenerMode, List<FunctionData> methodInfos, Type[] customArgTypes = null)
        {
            Type objectType = targetObject.GetType();

            Type[] argTypes;

            if (listenerMode == PersistentListenerMode.EventDefined && customArgTypes != null)
                argTypes = customArgTypes;
            else
                argTypes = GetTypeForListenerMode(listenerMode);

            var foundMethods = new List<MethodInfo>();

            // For some reason BindingFlags.FlattenHierarchy does not seem to work, so we manually traverse the base types instead
            while (objectType != null)
            {
                MethodInfo[] foundMethodsOnType =
                    objectType.GetMethods(BindingFlags.Public | (CachedSettings.privateMembersShown ? BindingFlags.NonPublic : BindingFlags.Default) | BindingFlags.Instance);

                foundMethods.AddRange(foundMethodsOnType);

                objectType = objectType.BaseType;
            }

            foreach (MethodInfo methodInfo in foundMethods)
            {
                // Sadly we can only use functions with void return type since C# throws an error
                if (methodInfo.ReturnType != typeof(void))
                    continue;

                ParameterInfo[] methodParams = methodInfo.GetParameters();
                if (methodParams.Length != argTypes.Length)
                    continue;

                bool isValidParamMatch = true;
                for (int i = 0; i < methodParams.Length; i++)
                {
                    if (!methodParams[i].ParameterType.IsAssignableFrom(argTypes[i]) /* && (argTypes[i] != typeof(int) || !methodParams[i].ParameterType.IsEnum)*/) isValidParamMatch = false;
                    if (listenerMode == PersistentListenerMode.Object && argTypes[i].IsAssignableFrom(methodParams[i].ParameterType)) isValidParamMatch = true;
                }

                if (!isValidParamMatch)
                    continue;

                if (!CachedSettings.privateMembersShown && methodInfo.GetCustomAttributes(typeof(ObsoleteAttribute), true).Length > 0)
                    continue;


                var foundMethodData = new FunctionData(null, targetObject, methodInfo, listenerMode);

                methodInfos.Add(foundMethodData);
            }
        }

        /// <summary>
        ///     Returns a string name of the given <see cref="Type" />.
        /// </summary>
        /// <remarks>
        ///     Used instead of direct Type.Name call because it returns prettier values for built-in types (i.e. int instead of
        ///     System.Int32).
        /// </remarks>
        /// <param name="typeToName"><see cref="Type" /> to get the name for.</param>
        /// <returns>String name of the given <see cref="Type" />.</returns>
        protected static string GetTypeName(Type typeToName)
        {
            if (typeToName == typeof(float))
                return "float";
            if (typeToName == typeof(bool))
                return "bool";
            if (typeToName == typeof(int))
                return "int";
            if (typeToName == typeof(string))
                return "string";

            return typeToName.Name;
        }

        #endregion

        #region Fields

        /// <summary>
        ///     The height of the header.
        /// </summary>
        protected const float HeaderHeight = 20f;

        /// <summary>
        ///     The padding for the drawer.
        /// </summary>
        protected const float Padding = 6f;

        /// <summary>
        ///     The offset of the drawer height.
        /// </summary>
        protected const float HeightOffset = 2f;

        /// <summary>
        ///     The header background style.
        /// </summary>
        protected readonly GUIStyle headerBackground = new GUIStyle("RL Header");

        /// <summary>
        ///     TODO: docs
        /// </summary>
        protected readonly Dictionary<string, DrawerState> drawerStates = new Dictionary<string, DrawerState>();

        /// <summary>
        ///     TODO: docs
        /// </summary>
        protected DrawerState currentState;

        /// <summary>
        ///     TODO: docs
        /// </summary>
        protected string currentLabelText;

        /// <summary>
        ///     TODO: docs
        /// </summary>
        protected SerializedProperty currentProperty;

        /// <summary>
        ///     Array of listeners of this <see cref="UnityEvent" />.
        /// </summary>
        protected SerializedProperty listenerArray;

        /// <summary>
        ///     TODO: docs
        /// </summary>
        protected UnityEventBase dummyEvent;

        /// <summary>
        ///     TODO: docs
        /// </summary>
        protected MethodInfo cachedFindMethodInfo;

        /// <summary>
        /// Reorderable list used to display this UnityEvent's listeners in the Inspector.
        /// </summary>
        protected ReorderableList ListenersList => currentState.reorderableList;

        #endregion

        #region Methods (PropertyDrawer Overrides)

        /// <inheritdoc />
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            currentLabelText = label.text;
            PrepareState(property);

            HandleKeyboardShortcuts();
            
            // We cannot draw listeners if dummyEvent is not initialized
            if (dummyEvent == null) return;

            // Draw header foldout
            DrawEventFoldout(position, property);
            
            // Draw the list of listeners if the event is expanded
            if (property.isExpanded && ListenersList != null)
            {
                // Draw list itself
                int oldIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                int gulp = Random.Range(0, 1000000);
                GUI.SetNextControlName($"bubbles {this.GetHashCode()}");
                ListenersList.DoList(position);
                EditorGUI.indentLevel = oldIndent;
                
                // Make list grab focus if it is empty and the user clicked into it
                DrawEmptyListFocusableArea(position);
            }
        }

        /// <inheritdoc />
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Don't draw contents if not expanded
            if (!property.isExpanded) return EditorGUIUtility.singleLineHeight + HeightOffset * 2f;

            PrepareState(property);

            float height = 0f;
            if (ListenersList != null)
                height = ListenersList.GetHeight();

            return height;
        }

        #endregion

        #region Methods (Drawing ReorderableList)
        
        // This region contains methods which actually draw the collapsible UnityEvent and a reorderable list of all its listeners in the Inspector.
        
        /// <summary>
        /// Draws an empty focusable area over an empty ReorderableList so that it can get in focus even without any elements in it.
        /// </summary>
        /// <remarks>
        /// It saves time in Editor, because you can now copy listeners directly into the empty list.
        /// </remarks>
        /// <param name="position">Position of this UnityEvent PropertyDrawer.</param>
        private void DrawEmptyListFocusableArea(Rect position)
        {
            if (ListenersList.count > 0) return; 
            
            // Capture mouse clicks in the area to make list focused
            int horizontalOffset = 2;
            int verticalOffset = 2;
            var focusAreaRect = new Rect(position.x + horizontalOffset, position.y + HeightOffset + HeaderHeight + verticalOffset, position.width - horizontalOffset - 1, ListenersList.elementHeight);
            var currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                if (focusAreaRect.Contains(currentEvent.mousePosition))
                {
                    currentEvent.Use();
                    ListenersList.GrabKeyboardFocus();
                }
            }
                   
            // Draw the area itself if the list is focused 
            if (ListenersList.HasKeyboardControl())
            {
                var focusAreaColor = ListenersList.HasKeyboardControl() ? FocusedColor : Color.clear;
                EditorGUI.DrawRect(focusAreaRect, focusAreaColor);
                var emptyListLabelRect = new Rect(focusAreaRect.x + 5, focusAreaRect.y, focusAreaRect.width, focusAreaRect.height);
                EditorGUI.LabelField(emptyListLabelRect, "List is Empty", EditorStyles.label);
            }
            
            // Context menu is available for empty list
            HandleContextMenu(focusAreaRect, 0);
        }

        /// <summary>
        /// Draws a foldout header for this UnityEvent.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="property"></param>
        private void DrawEventFoldout(Rect position, SerializedProperty property)
        {
            // Get foldout dimensions
            var foldoutPosition = new Rect(position.x, position.y + HeightOffset - 2, position.width, HeaderHeight);

            property.isExpanded = EditorGUI.Foldout(foldoutPosition, property.isExpanded, GUIContent.none, true);

            // Draw the default header if the event is collapsed
            if (!property.isExpanded)
            {
                // Draw event header background
                if (Event.current.type == EventType.Repaint) headerBackground.Draw(position, false, false, false, false);

                // Draw event header title
                var headerTitle = new GUIContent(string.IsNullOrEmpty(currentLabelText) ? "Event" : $"{currentLabelText} {GetEventParamsStr(dummyEvent)}");
                position.x += 6;
                position.y -= 1;
                GUI.Label(position, headerTitle);
            }
        }

        /// <summary>
        ///     Draws the header element of the UnityEvent field.
        /// </summary>
        /// <param name="headerRect">Element rect.</param>
        private void DrawHeaderCallback(Rect headerRect)
        {
            // We need to know where to position the invoke field based on the length of the title in the UI
            var headerTitle = new GUIContent(string.IsNullOrEmpty(currentLabelText) ? "Event" : currentLabelText + " " + GetEventParamsStr(dummyEvent));
            float headerStartOffset = EditorStyles.label.CalcSize(headerTitle).x;

            // Draw event title
            GUI.Label(headerRect, headerTitle);
        }

        /// <summary>
        ///     Draws a single element of the UnityEvent listeners list.
        /// </summary>
        /// <param name="rect">Element rect.</param>
        /// <param name="index">List index of the element being drawn.</param>
        /// <param name="active">Is this list element currently active?</param>
        /// <param name="focused">Is this list element currently active?</param>
        protected virtual void DrawEventListenerCallback(Rect rect, int index, bool active, bool focused)
        {
            var element = listenerArray.GetArrayElementAtIndex(index);

            rect.y++;
            var rects = GetEventListenerRects(rect);
                
            // Context menu
            HandleContextMenu(rect, index);

            var enabledRect = rects[0];
            var gameObjectRect = rects[1];
            var functionRect = rects[2];
            var argRect = rects[3];

            var serializedCallState = element.FindPropertyRelative("m_CallState");
            var serializedMode = element.FindPropertyRelative("m_Mode");
            var serializedArgs = element.FindPropertyRelative("m_Arguments");
            var serializedTarget = element.FindPropertyRelative("m_Target");
            var serializedMethod = element.FindPropertyRelative("m_MethodName");

            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor = Color.white;

            EditorGUI.PropertyField(enabledRect, serializedCallState, GUIContent.none);

            EditorGUI.BeginChangeCheck();

            var oldTargetObject = serializedTarget.objectReferenceValue;

            GUI.Box(gameObjectRect, GUIContent.none);
            EditorGUI.PropertyField(gameObjectRect, serializedTarget, GUIContent.none);
            if (EditorGUI.EndChangeCheck())
            {
                var newTargetObject = serializedTarget.objectReferenceValue;

                // Attempt to maintain the function pointer and component pointer if someone changes the target object and it has the correct component type on it.
                if (oldTargetObject != null && newTargetObject != null)
                {
                    if (oldTargetObject.GetType() != newTargetObject.GetType()) // If not an asset, if it is an asset and the same type we don't do anything
                    {
                        // If these are Unity components then the game object that they are attached to may have multiple copies of the same component type so attempt to match the count
                        if (typeof(Component).IsAssignableFrom(oldTargetObject.GetType()) && newTargetObject.GetType() == typeof(GameObject))
                        {
                            GameObject oldParentObject = ((Component) oldTargetObject).gameObject;
                            var newParentObject = (GameObject) newTargetObject;

                            Component[] oldComponentList = oldParentObject.GetComponents(oldTargetObject.GetType());

                            int componentLocationOffset = 0;
                            foreach (var oldComponent in oldComponentList)
                            {
                                if (oldComponent == oldTargetObject)
                                    break;

                                if (oldComponent.GetType() == oldTargetObject.GetType())
                                    // Only take exact matches for component type since I don't want to do redo the reflection to find the methods at the moment.
                                    componentLocationOffset++;
                            }

                            Component[] newComponentList = newParentObject.GetComponents(oldTargetObject.GetType());

                            int newComponentIndex = 0;
                            int componentCount = -1;
                            for (int i = 0; i < newComponentList.Length; ++i)
                            {
                                if (componentCount == componentLocationOffset)
                                    break;

                                if (newComponentList[i].GetType() != oldTargetObject.GetType()) continue;
                                
                                newComponentIndex = i;
                                componentCount++;
                            }

                            if (newComponentList.Length > 0 && newComponentList[newComponentIndex].GetType() == oldTargetObject.GetType())
                                serializedTarget.objectReferenceValue = newComponentList[newComponentIndex];
                            else
                                serializedMethod.stringValue = null;
                        }
                        else { serializedMethod.stringValue = null; }
                    }
                }
                else { serializedMethod.stringValue = null; }
            }

            var mode = (PersistentListenerMode) serializedMode.enumValueIndex;

            SerializedProperty argument;
            if (serializedTarget.objectReferenceValue == null || string.IsNullOrEmpty(serializedMethod.stringValue))
                mode = PersistentListenerMode.Void;

            switch (mode)
            {
                case PersistentListenerMode.Object:
                case PersistentListenerMode.String:
                case PersistentListenerMode.Bool:
                case PersistentListenerMode.Float:
                    argument = serializedArgs.FindPropertyRelative($"m_{Enum.GetName(typeof(PersistentListenerMode), mode)}Argument");
                    break;
                default:
                    argument = serializedArgs.FindPropertyRelative("m_IntArgument");
                    break;
            }

            string argTypeName = serializedArgs.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue;
            Type argType = typeof(Object);
            if (!string.IsNullOrEmpty(argTypeName))
                argType = ReorderableUnityEventHandler.FindTypeInAllAssemblies(argTypeName) ?? typeof(Object);

            if (mode == PersistentListenerMode.Object)
            {
                EditorGUI.BeginChangeCheck();
                Object result = EditorGUI.ObjectField(argRect, GUIContent.none, argument.objectReferenceValue, argType, true);
                if (EditorGUI.EndChangeCheck())
                    argument.objectReferenceValue = result;
            }
            else if (mode != PersistentListenerMode.Void && mode != PersistentListenerMode.EventDefined) { EditorGUI.PropertyField(argRect, argument, GUIContent.none); }

            EditorGUI.BeginDisabledGroup(serializedTarget.objectReferenceValue == null);
            {
                EditorGUI.BeginProperty(functionRect, GUIContent.none, serializedMethod);

                GUIContent buttonContent;

                if (EditorGUI.showMixedValue) { buttonContent = new GUIContent("\u2014", "Mixed Values"); }
                else
                {
                    if (serializedTarget.objectReferenceValue == null || string.IsNullOrEmpty(serializedMethod.stringValue))
                        buttonContent = new GUIContent("No Function");
                    else
                        buttonContent = new GUIContent(GetFunctionDisplayName(serializedTarget, serializedMethod, mode, argType, CachedSettings.argumentTypeDisplayed));
                }

                if (GUI.Button(functionRect, buttonContent, EditorStyles.popup)) BuildPopupMenu(serializedTarget.objectReferenceValue, element /*, argType*/).DropDown(functionRect);

                EditorGUI.EndProperty();
            }
            EditorGUI.EndDisabledGroup();
        }

        /// <summary>
        ///     Called when a listener of the UnityEvent becomes selected.
        /// </summary>
        /// <param name="list">Reorderable list which sent the callback.</param>
        protected virtual void SelectEventListenerCallback(ReorderableList list)
        {
            currentState.lastSelectedIndex = list.index;
        }

        /// <summary>
        ///     Called when a new listener gets added to UnityEvent.
        /// </summary>
        /// <param name="list">Reorderable list which sent the callback.</param>
        protected virtual void AddEventListenerCallback(ReorderableList list)
        {
            if (listenerArray.hasMultipleDifferentValues)
            {
                foreach (Object targetObj in listenerArray.serializedObject.targetObjects)
                {
                    var tempSerializedObject = new SerializedObject(targetObj);
                    SerializedProperty listenerArrayProperty = tempSerializedObject.FindProperty(listenerArray.propertyPath);
                    listenerArrayProperty.arraySize += 1;
                    tempSerializedObject.ApplyModifiedProperties();
                }

                listenerArray.serializedObject.SetIsDifferentCacheDirty();
                listenerArray.serializedObject.Update();
                list.index = list.serializedProperty.arraySize - 1;
            }
            else { ReorderableList.defaultBehaviours.DoAddButton(list); }

            currentState.lastSelectedIndex = list.index;

            // Init default state
            SerializedProperty serialiedListener = listenerArray.GetArrayElementAtIndex(list.index);
            ResetEventState(serialiedListener);
        }

        /// <summary>
        ///     Called when listeners of UnityEvent get reordered.
        /// </summary>
        /// <param name="list">Reorderable list which sent the callback.</param>
        protected virtual void ReorderCallback(ReorderableList list)
        {
            currentState.lastSelectedIndex = list.index;
        }

        /// <summary>
        ///     Called when a listener gets removed from UnityEvent.
        /// </summary>
        /// <param name="list">Reorderable list which sent the callback.</param>
        protected virtual void RemoveCallback(ReorderableList list)
        {
            if (ListenersList.count <= 0) return;
            
            ReorderableList.defaultBehaviours.DoRemoveButton(list);
            currentState.lastSelectedIndex = list.index;
        }

        #endregion
        
        #region Methods (Clipboard)

        // This region contains clipboard functionality for copying, cutting and pasting event listeners between UnityEvents.

        /// <summary>
        ///     Data container for storing copied event listener in clipboard.
        /// </summary>
        private static class EventListenerClipboardStorage
        {
            private static EventListenerData CopiedEventListenerData;
            
            /// <summary>
            /// True if the clipboard is empty, false otherwise.
            /// </summary>
            public static bool IsEmpty => CopiedEventListenerData == null;
            
            /// <summary>
            /// Stores event listener data from the given listener in the clipboard.
            /// </summary>
            /// <param name="sourceListenerProperty"><see cref="SerializedProperty"/> of the event listener to store.</param>
            public static void Store(SerializedProperty sourceListenerProperty)
            {
                CopiedEventListenerData = new EventListenerData();

                CopiedEventListenerData.callState = sourceListenerProperty.FindPropertyRelative("m_CallState").enumValueIndex;
                CopiedEventListenerData.target = sourceListenerProperty.FindPropertyRelative("m_Target").objectReferenceValue;
                CopiedEventListenerData.methodName = sourceListenerProperty.FindPropertyRelative("m_MethodName").stringValue;
                CopiedEventListenerData.mode = sourceListenerProperty.FindPropertyRelative("m_Mode").enumValueIndex;

                SerializedProperty sourceListenerArgs = sourceListenerProperty.FindPropertyRelative("m_Arguments");
                CopiedEventListenerData.intArgument = sourceListenerArgs.FindPropertyRelative("m_IntArgument").intValue;
                CopiedEventListenerData.floatArgument = sourceListenerArgs.FindPropertyRelative("m_FloatArgument").floatValue;
                CopiedEventListenerData.boolArgument = sourceListenerArgs.FindPropertyRelative("m_BoolArgument").boolValue;
                CopiedEventListenerData.stringArgument = sourceListenerArgs.FindPropertyRelative("m_StringArgument").stringValue;
                CopiedEventListenerData.objectArgument = sourceListenerArgs.FindPropertyRelative("m_ObjectArgument").objectReferenceValue;
                CopiedEventListenerData.objectArgumentAssemblyTypeName = sourceListenerArgs.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue;
            }

            /// <summary>
            /// Extracts event listener data from clipboard (if any) and applies it to the given listener.
            /// </summary>
            /// <param name="targetListenerProperty"><see cref="SerializedProperty"/> of the event listener to apply clipboard data to.</param>
            public static void Extract(SerializedProperty targetListenerProperty)
            {
                if (CopiedEventListenerData == null) return;
                
                targetListenerProperty.FindPropertyRelative("m_CallState").enumValueIndex = CopiedEventListenerData.callState;
                targetListenerProperty.FindPropertyRelative("m_Target").objectReferenceValue = CopiedEventListenerData.target;
                targetListenerProperty.FindPropertyRelative("m_MethodName").stringValue = CopiedEventListenerData.methodName;
                targetListenerProperty.FindPropertyRelative("m_Mode").enumValueIndex = CopiedEventListenerData.mode;

                SerializedProperty targetArgs = targetListenerProperty.FindPropertyRelative("m_Arguments");

                targetArgs.FindPropertyRelative("m_IntArgument").intValue = CopiedEventListenerData.intArgument;
                targetArgs.FindPropertyRelative("m_FloatArgument").floatValue = CopiedEventListenerData.floatArgument;
                targetArgs.FindPropertyRelative("m_BoolArgument").boolValue = CopiedEventListenerData.boolArgument;
                targetArgs.FindPropertyRelative("m_StringArgument").stringValue = CopiedEventListenerData.stringArgument;
                targetArgs.FindPropertyRelative("m_ObjectArgument").objectReferenceValue = CopiedEventListenerData.objectArgument;
                targetArgs.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = CopiedEventListenerData.objectArgumentAssemblyTypeName;

            }
            
            /// <summary>
            /// Struct to store data of a single <see cref="UnityEvent"/> listener.
            /// </summary>
            /// <remarks>
            /// We could copy reference to <see cref="SerializedProperty"/> of the original event listener, but
            /// it will mean that the clipboard will lose this reference when the original event listener is deleted.
            /// We need to persist copied listener data, so we put it into a separate data container. 
            /// </remarks>
            private class EventListenerData
            {
                public int callState;
                public Object target;
                public string methodName;
                public int mode;

                public int intArgument;
                public float floatArgument;
                public bool boolArgument;
                public string stringArgument;
                public Object objectArgument;
                public string objectArgumentAssemblyTypeName;
            }
        }

        /// <summary>
        ///     Handles copying currently selected listener to clipboard.
        /// </summary>
        private void HandleCopy()
        {
            if (ListenersList.count == 0) return;
            
            int listenerIndex = ListenersList.index;
            EventListenerClipboardStorage.Store(listenerArray.GetArrayElementAtIndex(listenerIndex));
        }

        /// <summary>
        ///     Handles pasting listener from clipboard to the end of this UnityEvent listeners list.
        /// </summary>
        /// <remarks>
        ///     New listener is pasted in the list right after currently selected listener (if any).
        /// </remarks>
        private void HandlePaste()
        {
            if (EventListenerClipboardStorage.IsEmpty) return;
            
            int targetArrayIdx = Mathf.Max(ListenersList.index, 0);
            ListenersList.serializedProperty.InsertArrayElementAtIndex(targetArrayIdx);
            
            var targetProperty = ListenersList.serializedProperty.GetArrayElementAtIndex(ListenersList.count > 1 ? ListenersList.index + 1 : 0);
            ResetEventState(targetProperty);
            
            EventListenerClipboardStorage.Extract(targetProperty);
            
            ListenersList.index++;
            currentState.lastSelectedIndex++;

            targetProperty.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        ///     Handles cutting currently selected listener to clipboard.
        /// </summary>
        private void HandleCut()
        {
            HandleCopy();
            HandleDelete();
        }

        /// <summary>
        ///     Handles duplicating currently selected listener in this UnityEvent listeners list.
        /// </summary>
        /// <remarks>
        ///     Duplicated listener is pasted in the list right after the original listener.
        /// </remarks>
        private void HandleDuplicate()
        {
            HandleCopy();
            HandlePaste();
        }

        /// <summary>
        ///     Handles deleting currently selected listener in this UnityEvent listeners list.
        /// </summary>
        protected void HandleDelete()
        {
            if (ListenersList.count == 0) return;

            RemoveCallback(ListenersList);
        }
        
        #endregion
        
        #region Methods (Keyboard Shortcuts)

        // This region contains methods which handle keyboard shortcuts (like Ctrl+C, Ctrl+V).

        /// <summary>
        ///     Handles all keyboard shortcuts.
        /// </summary>
        private void HandleKeyboardShortcuts()
        {
            if (!CachedSettings.hotkeysEnabled)
                return;

            Event currentEvent = Event.current;

            if (!ListenersList.HasKeyboardControl())
                return;

            if (currentEvent.type == EventType.ValidateCommand)
            {
                if (currentEvent.commandName == "Copy" ||
                    currentEvent.commandName == "Paste" ||
                    currentEvent.commandName == "Cut" ||
                    currentEvent.commandName == "Duplicate" ||
                    currentEvent.commandName == "Delete" ||
                    currentEvent.commandName == "SoftDelete" /*|| // NOTE: no more using Ctrl+A to add new, as it's an obscure shortcut (Ctrl+A is usually used to select all).
                currentEvent.commandName == "SelectAll"*/)
                    currentEvent.Use();
            }
            else if (currentEvent.type == EventType.ExecuteCommand)
            {
                // Execute selected command
                switch (currentEvent.commandName)
                {
                    case "Copy":
                    {
                        HandleCopy();
                        currentEvent.Use();
                        break;
                    }
                    case "Paste":
                    {
                        HandlePaste();
                        currentEvent.Use();
                        break;
                    }
                    case "Cut":
                    {
                        HandleCut();
                        currentEvent.Use();
                        break;
                    }
                    case "Duplicate":
                    {
                        HandleDuplicate();
                        currentEvent.Use();
                        break;
                    }
                    case "Delete":
                    case "SoftDelete":
                    {
                        HandleDelete();
                        currentEvent.Use();
                        break;
                    }
                }

                // Apply modified properties to the list
                SerializedProperty listProperty = ListenersList.serializedProperty;
                listProperty.serializedObject.ApplyModifiedProperties();
            }
        }

        #endregion

        #region Methods (Debugger)

        /// <summary>
        /// Makes it possible to right-click the given rect to summon a context menu for adding a debugging callback to this UnityEvent.
        /// </summary>
        /// <param name="rect">Rect of the area to detect right click in.</param>
        private void HandleContextMenu(Rect rect, int listenerIndex)
        {
            var currentEvent = Event.current;
            
            // If right mouse button was not released this frame, not displaying the menu
            if (currentEvent.type != EventType.MouseUp || currentEvent.button != 1) return;
            
            // If right click was registered not over the given rect, not displaying the menu
            if (!rect.Contains(currentEvent.mousePosition)) return;
            
            ListenersList.index = listenerIndex;
            currentEvent.Use();
            
            // create the menu and add items to it
            var menu = new GenericMenu();

            if (ListenersList.count > 0)
            {
                // menu.AddItem(new GUIContent("Cut Callback"), false, HandleCut); // TODO: deletion doesn't work from here; fix later
                menu.AddItem(new GUIContent("Copy Callback"), false, HandleCopy);
                if (!EventListenerClipboardStorage.IsEmpty)
                {
                    menu.AddItem(new GUIContent("Paste Callback"), false, HandlePaste);
                }
                
                menu.AddSeparator("");
                
                menu.AddItem(new GUIContent("Duplicate Callback"), false, HandleDuplicate);
                // menu.AddItem(new GUIContent("Delete Callback"), false, HandleDelete); // TODO: deletion doesn't work from here; fix later
                
                menu.AddSeparator("");
            }
            
            // Debug callback
            // if (false) // TODO: disable this menu item when one debugger callback is already present
            // {
            //     menu.AddDisabledItem(new GUIContent("Add Debug Callback"));
            // }
            // else
            {
                menu.AddItem(new GUIContent("Add Debug Callback"), false, AddDebugger);
            }

            // display the menu
            menu.ShowAsContext();
        }
        
        private void AddDebugger()
        {
            // Find debugger in the same scene as this UnityEvent is in
            UnityEventDebugger debugger = null;
            var allDebuggers = GameObject.FindObjectsOfType<UnityEventDebugger>();
            var eventScene = GetThisEventScene();
            foreach (var candidateDebugger in allDebuggers)
            {
                if (candidateDebugger.gameObject.scene != eventScene) continue;
                debugger = candidateDebugger;
            }
            
            // If no debugger found, create it manually
            if (debugger == null)
            {
                var debuggerObject = new GameObject("Unity Event Debugger")
                {
                    hideFlags = HideFlags.NotEditable
                };
                debugger = debuggerObject.AddComponent<UnityEventDebugger>();
                
                // Move debugger to the scene this UnityEvent belongs to
                SceneManager.MoveGameObjectToScene(debuggerObject, eventScene);
            }
            
            // Add new callback with debugger in it
            ListenersList.serializedProperty.InsertArrayElementAtIndex(0);
            
            var targetProperty = ListenersList.serializedProperty.GetArrayElementAtIndex(0);
            ResetEventState(targetProperty);

            var serializedTarget = targetProperty.FindPropertyRelative("m_Target");
            serializedTarget.objectReferenceValue = debugger;
            
            ListenersList.index = 0;
            currentState.lastSelectedIndex = ListenersList.index;

            targetProperty.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Returns Scene to which this UnityEvent belongs to (if any).
        /// Used for setting a proper reference to UnityEvent debugger.
        /// </summary>
        /// <remarks>
        /// This is a bit of hacky shortcut, and is not guaranteed to return the same scene as this UnityEvent 
        /// belongs to if there are multiple scenes open and the selected GameObject is in another scene.
        /// Here we just assume that the selected object will be the object this UnityEvent is on, as this function
        /// gets called only when adding debug logger (and to add debug logger you have to select this GameObject anyways).
        /// </remarks>
        /// <returns></returns>
        private Scene GetThisEventScene()
        {
            return Selection.activeGameObject.scene;
        }

        #endregion
        
        #region Methods (Utility)

        /// <summary>
        ///     TODO: docs
        /// </summary>
        /// <param name="propertyForState"></param>
        private void PrepareState(SerializedProperty propertyForState)
        {
            DrawerState state;

            if (!drawerStates.TryGetValue(propertyForState.propertyPath, out state))
            {
                state = new DrawerState();

                SerializedProperty persistentListeners = propertyForState.FindPropertyRelative("m_PersistentCalls.m_Calls");

                // The fun thing is that if Unity just made the first bool arg true internally, this whole thing would be unnecessary.
                state.reorderableList = new ReorderableList(propertyForState.serializedObject, persistentListeners, true, true, true, true);
                state.reorderableList.elementHeight = 43; // todo: actually find proper constant for this. 
                state.reorderableList.drawHeaderCallback += DrawHeaderCallback;
                state.reorderableList.drawElementCallback += DrawEventListenerCallback;
                state.reorderableList.onSelectCallback += SelectEventListenerCallback;
                state.reorderableList.onRemoveCallback += ReorderCallback;
                state.reorderableList.onAddCallback += AddEventListenerCallback;
                state.reorderableList.onRemoveCallback += RemoveCallback;

                state.lastSelectedIndex = 0;

                drawerStates.Add(propertyForState.propertyPath, state);
            }

            currentProperty = propertyForState;

            currentState = state;
            ListenersList.index = currentState.lastSelectedIndex;
            listenerArray = state.reorderableList.serializedProperty;

            // Setup dummy event
#if UNITY_2018_4_OR_NEWER
            dummyEvent = GetDummyEvent(propertyForState);
#else
        string eventTypeName = currentProperty.FindPropertyRelative("m_TypeName").stringValue;
        System.Type eventType = ReorderableUnityEventHandler.FindTypeInAllAssemblies(eventTypeName);
        if (eventType == null)
            dummyEvent = new UnityEvent();
        else
            dummyEvent = System.Activator.CreateInstance(eventType) as UnityEventBase;
#endif

            CachedSettings = ReorderableUnityEventHandler.GetEditorSettings();
        }

        /// <summary>
        ///     TODO: docs
        /// </summary>
        /// <param name="functionName"></param>
        /// <param name="targetObject"></param>
        /// <param name="eventObject"></param>
        /// <param name="listenerMode"></param>
        /// <param name="argType"></param>
        /// <returns></returns>
        private MethodInfo InvokeFindMethod(string functionName, object targetObject, UnityEventBase eventObject, PersistentListenerMode listenerMode, Type argType = null)
        {
            MethodInfo findMethod = cachedFindMethodInfo;

            if (findMethod == null)
            {
                // Rather not reinvent the wheel considering this function calls different functions depending on the number of args the event has...
                // Unity 2020.1 changed the function signature for the FindMethod method (the second parameter is a Type instead of an object)
                // Source: https://github.com/MerlinVR/EasyEventEditor/issues/10
                findMethod = eventObject.GetType().GetMethod("FindMethod", BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[]
                    {
                        typeof(string),
                    #if UNITY_2020_1_OR_NEWER
                        typeof(Type),
                    #else
                        typeof(object),
                    #endif
                        typeof(PersistentListenerMode),
                        typeof(Type)
                    },
                    null);

                cachedFindMethodInfo = findMethod;
            }

            if (findMethod == null)
            {
                Debug.LogError("Could not find FindMethod function!");
                return null;
            }

        #if UNITY_2020_1_OR_NEWER
            return findMethod.Invoke(eventObject, new object[] {functionName, targetObject?.GetType(), listenerMode, argType }) as MethodInfo;
        #else
            return findMethod.Invoke(eventObject, new object[] {functionName, targetObject, listenerMode, argType }) as MethodInfo;
        #endif
        }

        private Type[] GetEventParams(UnityEventBase eventIn)
        {
            MethodInfo methodInfo = InvokeFindMethod("Invoke", eventIn, eventIn, PersistentListenerMode.EventDefined);
            return methodInfo.GetParameters().Select(x => x.ParameterType).ToArray();
        }

        protected string GetEventParamsStr(UnityEventBase eventIn)
        {
            var builder = new StringBuilder();
            Type[] methodTypes = GetEventParams(eventIn);

            // Get pretty type name for each argument
            int typeCount = methodTypes.Length;
            var methodTypeStrings = new string[typeCount];
            for (int i = 0; i < typeCount; i++) { methodTypeStrings[i] = GetTypeName(methodTypes[i]); }

            builder.Append("(");
            builder.Append(string.Join(", ", methodTypeStrings));
            builder.Append(")");

            return builder.ToString();
        }

        /// <summary>
        ///     Returns a pre-formatted string of the arguments of the given function.
        /// </summary>
        /// <param name="functionName"></param>
        /// <param name="targetObject"></param>
        /// <param name="listenerMode"></param>
        /// <param name="argType"></param>
        /// <returns></returns>
        protected string GetFunctionArgStr(string functionName, object targetObject, PersistentListenerMode listenerMode, Type argType = null)
        {
            MethodInfo methodInfo = InvokeFindMethod(functionName, targetObject, dummyEvent, listenerMode, argType);

            if (methodInfo == null)
                return "";

            ParameterInfo[] parameterInfos = methodInfo.GetParameters();
            if (parameterInfos.Length == 0)
                return "";

            return GetTypeName(parameterInfos[0].ParameterType);
        }

        /// <summary>
        ///     Returns a display name for the given listener callback method.
        /// </summary>
        /// <param name="objectProperty">Object which contains the method.</param>
        /// <param name="methodProperty">Method to get the name of.</param>
        /// <param name="listenerMode"><see cref="PersistentListenerMode" /> of the listener.</param>
        /// <param name="argType">Method argument type.</param>
        /// <param name="showArg">If true, method arguments will be added to </param>
        /// <returns></returns>
        protected string GetFunctionDisplayName(SerializedProperty objectProperty, SerializedProperty methodProperty, PersistentListenerMode listenerMode, Type argType, bool showArg)
        {
            string methodNameOut = "No Function";

            if (objectProperty.objectReferenceValue == null || methodProperty.stringValue == "")
                return methodNameOut;

            MethodInfo methodInfo = InvokeFindMethod(methodProperty.stringValue, objectProperty.objectReferenceValue, dummyEvent, listenerMode, argType);
            string funcName = methodProperty.stringValue.StartsWith("set_") ? methodProperty.stringValue.Substring(4) : methodProperty.stringValue;

            if (methodInfo == null)
            {
                methodNameOut = $"<Missing {objectProperty.objectReferenceValue.GetType().Name}.{funcName}>";
                return methodNameOut;
            }

            string objectTypeName = objectProperty.objectReferenceValue.GetType().Name;
            var objectComponent = objectProperty.objectReferenceValue as Component;

            if (!CachedSettings.sameComponentTypesGrouped && objectComponent != null)
            {
                Type objectType = objectProperty.objectReferenceValue.GetType();

                Component[] components = objectComponent.GetComponents(objectType);

                if (components.Length > 1)
                {
                    int componentID = 0;
                    for (int i = 0; i < components.Length; i++)
                    {
                        if (components[i] == objectComponent)
                        {
                            componentID = i + 1;
                            break;
                        }
                    }

                    objectTypeName += $"({componentID})";
                }
            }

            if (showArg)
            {
                string functionArgStr = GetFunctionArgStr(methodProperty.stringValue, objectProperty.objectReferenceValue, listenerMode, argType);
                methodNameOut = $"{objectTypeName}.{funcName} ({functionArgStr})";
            }
            else { methodNameOut = $"{objectTypeName}.{funcName}"; }


            return methodNameOut;
        }

        protected void AddFunctionToMenu(string contentPath, SerializedProperty elementProperty, FunctionData methodData, GenericMenu menu, int componentCount, bool dynamicCall = false)
        {
            string functionName = methodData.targetMethod.Name.StartsWith("set_") ? methodData.targetMethod.Name.Substring(4) : methodData.targetMethod.Name;
            string argStr = string.Join(", ", methodData.targetMethod.GetParameters().Select(param => GetTypeName(param.ParameterType)).ToArray());

            if (dynamicCall) // Cut out the args from the dynamic variation to match Unity, and the menu item won't be created if it's not unique.
            {
                contentPath += functionName;
            }
            else
            {
                if (methodData.targetMethod.Name.StartsWith("set_")) // If it's a property add the arg before the name
                    contentPath += argStr + " " + functionName;
                else
                    contentPath += functionName + " (" + argStr + ")"; // Add arguments
            }

            if (!methodData.targetMethod.IsPublic)
                contentPath += " " + (methodData.targetMethod.IsPrivate ? "<private>" : "<internal>");

            if (methodData.targetMethod.GetCustomAttributes(typeof(ObsoleteAttribute), true).Length > 0)
                contentPath += " <obsolete>";

            methodData.listenerElement = elementProperty;

            SerializedProperty serializedTargetObject = elementProperty.FindPropertyRelative("m_Target");
            SerializedProperty serializedMethodName = elementProperty.FindPropertyRelative("m_MethodName");
            SerializedProperty serializedMode = elementProperty.FindPropertyRelative("m_Mode");

            bool itemOn = serializedTargetObject.objectReferenceValue == methodData.targetObject &&
                          serializedMethodName.stringValue == methodData.targetMethod.Name &&
                          serializedMode.enumValueIndex == (int) methodData.listenerMode;

            menu.AddItem(new GUIContent(contentPath), itemOn, SetEventFunctionCallback, methodData);
        }

        /// <summary>
        ///     Builds a popup menu for selecting method callbacks from the object currently connected to the UnityEvent.
        ///     TODO: docs
        /// </summary>
        /// <param name="targetObj">TODO: docs</param>
        /// <param name="elementProperty">TODO: docs</param>
        /// <returns>TODO: docs</returns>
        protected GenericMenu BuildPopupMenu(Object targetObj, SerializedProperty elementProperty)
        {
            var menu = new GenericMenu();

            string currentMethodName = elementProperty.FindPropertyRelative("m_MethodName").stringValue;

            menu.AddItem(new GUIContent("No Function"), string.IsNullOrEmpty(currentMethodName), ClearEventFunctionCallback, new FunctionData(elementProperty));
            menu.AddSeparator("");

            if (targetObj is Component) { targetObj = (targetObj as Component).gameObject; }
            else if (!(targetObj is GameObject))
            {
                // Function menu for asset objects and such
                BuildMenuForObject(targetObj, elementProperty, menu);
                return menu;
            }

            // GameObject menu
            BuildMenuForObject(targetObj, elementProperty, menu);

            Component[] components = (targetObj as GameObject).GetComponents<Component>();
            var componentTypeCounts = new Dictionary<Type, ComponentTypeCount>();

            // Only get the first instance of each component type
            if (CachedSettings.sameComponentTypesGrouped)
                components = components.GroupBy(comp => comp.GetType()).Select(group => group.First()).ToArray();
            else // Otherwise we need to know if there are multiple components of a given type before we start going through the components since we only need numbers on component types with multiple instances.
                foreach (Component component in components)
                {
                    ComponentTypeCount typeCount;
                    if (!componentTypeCounts.TryGetValue(component.GetType(), out typeCount))
                    {
                        typeCount = new ComponentTypeCount();
                        componentTypeCounts.Add(component.GetType(), typeCount);
                    }

                    typeCount.totalCount++;
                }

            foreach (Component component in components)
            {
                int componentCount = 0;

                if (!CachedSettings.sameComponentTypesGrouped)
                {
                    ComponentTypeCount typeCount = componentTypeCounts[component.GetType()];
                    if (typeCount.totalCount > 1)
                        componentCount = typeCount.currentCount++;
                }

                BuildMenuForObject(component, elementProperty, menu, componentCount);
            }

            return menu;
        }

        protected void BuildMenuForObject(Object targetObject, SerializedProperty elementProperty, GenericMenu menu, int componentCount = 0)
        {
            var methodInfos = new List<FunctionData>();
            string contentPath = targetObject.GetType().Name + (componentCount > 0 ? string.Format("({0})", componentCount) : "") + "/";

            FindValidMethods(targetObject, PersistentListenerMode.Void, methodInfos);
            FindValidMethods(targetObject, PersistentListenerMode.Int, methodInfos);
            FindValidMethods(targetObject, PersistentListenerMode.Float, methodInfos);
            FindValidMethods(targetObject, PersistentListenerMode.String, methodInfos);
            FindValidMethods(targetObject, PersistentListenerMode.Bool, methodInfos);
            FindValidMethods(targetObject, PersistentListenerMode.Object, methodInfos);
            
            methodInfos = methodInfos.OrderBy(method1 => method1.targetMethod.Name.StartsWith("set_") ? 0 : 1).ThenBy(method1 => method1.targetMethod.Name).ToList();

            // Get event args to determine if we can do a pass through of the arg to the parameter
            Type[] eventArgs = dummyEvent.GetType().GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray();

            bool dynamicBinding = false;

            if (eventArgs.Length > 0)
            {
                var dynamicMethodInfos = new List<FunctionData>();
                FindValidMethods(targetObject, PersistentListenerMode.EventDefined, dynamicMethodInfos, eventArgs);

                if (dynamicMethodInfos.Count > 0)
                {
                    dynamicMethodInfos = dynamicMethodInfos.OrderBy(m => m.targetMethod.Name.StartsWith("set") ? 0 : 1).ThenBy(m => m.targetMethod.Name).ToList();

                    dynamicBinding = true;

                    // Add dynamic header
                    menu.AddDisabledItem(new GUIContent(contentPath + $"Dynamic {GetTypeName(eventArgs[0])}"));
                    menu.AddSeparator(contentPath);

                    foreach (FunctionData dynamicMethod in dynamicMethodInfos) { AddFunctionToMenu(contentPath, elementProperty, dynamicMethod, menu, 0, true); }
                }
            }

            // Add static header if we have dynamic bindings
            if (dynamicBinding)
            {
                menu.AddDisabledItem(new GUIContent(contentPath + "Static Parameters"));
                menu.AddSeparator(contentPath);
            }

            foreach (FunctionData method in methodInfos) { AddFunctionToMenu(contentPath, elementProperty, method, menu, componentCount); }
        }

        #endregion
    }

    [InitializeOnLoad]
    public class ReorderableUnityEventHandler
    {
        #region Data Types

        /// <summary>
        ///     Container for settings for <see cref="ReorderableUnityEventDrawer" />.
        /// </summary>
        public class ReorderableUnityEventSettings
        {
            /// <summary>
            ///     Whether <see cref="ReorderableUnityEventDrawer" /> is applied at all.
            /// </summary>
            public bool eventDrawerEnabled = true;

            /// <summary>
            ///     If enabled, private methods and properties will be exposed for event listeners when selecting a callback function
            ///     from the dropdown.
            /// </summary>
            public bool privateMembersShown = false;

            /// <summary>
            ///     If enabled, argument types will be shown alongside the method names in <see cref="UnityEvent" />s.
            /// </summary>
            public bool argumentTypeDisplayed = true;

            /// <summary>
            ///     If enabled and listener's target GameObject has several components of the same type, they will all be shown in the function selection dropdown.
            /// </summary>
            public bool sameComponentTypesGrouped = false;

            /// <summary>
            ///     If enabled, selected <see cref="UnityEvent" /> listeners can be cut, copied, pasted and duplicated using default Unity keyboard shortcuts.
            /// </summary>
            public bool hotkeysEnabled = true;
        }

        #endregion

        #region Constants

        private const string OverrideEventDrawerKey = "Games.NoSoySauce.DeveloperTools.ReorderableUnityEvents.ReorderableUnityEvent.overrideEventDrawer";
        private const string ShowPrivateMembersKey = "Games.NoSoySauce.DeveloperTools.ReorderableUnityEvents.ReorderableUnityEvent.showPrivateMembers";
        private const string ShowInvokeFieldKey = "Games.NoSoySauce.DeveloperTools.ReorderableUnityEvents.ReorderableUnityEvent.showInvokeField";
        private const string DisplayArgumentTypeKey = "Games.NoSoySauce.DeveloperTools.ReorderableUnityEvents.ReorderableUnityEvent.displayArgumentType";
        private const string GroupSameComponentTypeKey = "Games.NoSoySauce.DeveloperTools.ReorderableUnityEvents.ReorderableUnityEvent.groupSameComponentType";
        private const string UseHotkeys = "Games.NoSoySauce.DeveloperTools.ReorderableUnityEvents.ReorderableUnityEvent.usehotkeys";

        #endregion

        #region Static Fields

        private static bool DrawerPatchApplied;
        private static FieldInfo InternalDrawerTypeMap;
        private static Type AttributeUtilityType;

        #endregion

        #region Constructor

        static ReorderableUnityEventHandler()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        #endregion

        #region Static Events

        public static void ApplyEventPropertyDrawerPatch(bool forceApply = false)
        {
            ReorderableUnityEventSettings settings = GetEditorSettings();

            if (!DrawerPatchApplied || forceApply)
            {
                ApplyEventDrawerPatch(settings.eventDrawerEnabled);
                DrawerPatchApplied = true;
            }
        }

        // https://stackoverflow.com/questions/12898282/type-gettype-not-working 
        public static Type FindTypeInAllAssemblies(string qualifiedTypeName)
        {
            var t = Type.GetType(qualifiedTypeName);

            if (t != null) return t;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(qualifiedTypeName);
                if (t != null)
                    return t;
            }

            return null;
        }

        // TODO: store configuration in project instead of Editor
        public static ReorderableUnityEventSettings GetEditorSettings()
        {
            var settings = new ReorderableUnityEventSettings
            {
                eventDrawerEnabled = EditorPrefs.GetBool(OverrideEventDrawerKey, true),
                privateMembersShown = EditorPrefs.GetBool(ShowPrivateMembersKey, false),
                argumentTypeDisplayed = EditorPrefs.GetBool(DisplayArgumentTypeKey, true),
                sameComponentTypesGrouped = EditorPrefs.GetBool(GroupSameComponentTypeKey, false),
                hotkeysEnabled = EditorPrefs.GetBool(UseHotkeys, true)
            };

            return settings;
        }

        // TODO: store configuration in project instead of Editor
        public static void SetEditorSettings(ReorderableUnityEventSettings settings)
        {
            EditorPrefs.SetBool(OverrideEventDrawerKey, settings.eventDrawerEnabled);
            EditorPrefs.SetBool(ShowPrivateMembersKey, settings.privateMembersShown);
            EditorPrefs.SetBool(DisplayArgumentTypeKey, settings.argumentTypeDisplayed);
            EditorPrefs.SetBool(GroupSameComponentTypeKey, settings.sameComponentTypesGrouped);
            EditorPrefs.SetBool(UseHotkeys, settings.hotkeysEnabled);
        }

        /// <summary>
        ///     Applies
        /// </summary>
        private static void OnEditorUpdate()
        {
            ApplyEventPropertyDrawerPatch();
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            ApplyEventPropertyDrawerPatch(true);
        }

        private static FieldInfo GetDrawerTypeMap()
        {
            // We already have the map so skip all the reflection
            if (InternalDrawerTypeMap != null) return InternalDrawerTypeMap;

            Type scriptAttributeUtilityType = FindTypeInAllAssemblies("UnityEditor.ScriptAttributeUtility");

            if (scriptAttributeUtilityType == null)
            {
                Debug.LogError("Could not find ScriptAttributeUtility in assemblies!");
                return null;
            }

            // Save for later in case we need to lookup the function to populate the attributes
            AttributeUtilityType = scriptAttributeUtilityType;

            FieldInfo info = scriptAttributeUtilityType.GetField("s_DrawerTypeForType", BindingFlags.NonPublic | BindingFlags.Static);

            if (info == null)
            {
                Debug.LogError("Could not find drawer type map!");
                return null;
            }

            InternalDrawerTypeMap = info;

            return InternalDrawerTypeMap;
        }

        private static void ClearPropertyCaches()
        {
            if (AttributeUtilityType == null)
            {
                Debug.LogError("UnityEditor.ScriptAttributeUtility type is null! Make sure you have called GetDrawerTypeMap() to ensure this is cached!");
                return;
            }

            // Nuke handle caches so they can find our modified drawer
            MethodInfo clearCacheFunc = AttributeUtilityType.GetMethod("ClearGlobalCache", BindingFlags.NonPublic | BindingFlags.Static);

            if (clearCacheFunc == null)
            {
                Debug.LogError("Could not find cache clear method!");
                return;
            }

            clearCacheFunc.Invoke(null, new object[] { });

            FieldInfo currentCacheField = AttributeUtilityType.GetField("s_CurrentCache", BindingFlags.NonPublic | BindingFlags.Static);

            if (currentCacheField == null)
            {
                Debug.LogError("Could not find CurrentCache field!");
                return;
            }

            object currentCacheValue = currentCacheField.GetValue(null);

            if (currentCacheValue != null)
            {
                MethodInfo clearMethod = currentCacheValue.GetType().GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);

                if (clearMethod == null)
                {
                    Debug.LogError("Could not find clear function for current cache!");
                    return;
                }

                clearMethod.Invoke(currentCacheValue, new object[] { });
            }

            Type inspectorWindowType = FindTypeInAllAssemblies("UnityEditor.InspectorWindow");

            if (inspectorWindowType == null)
            {
                Debug.LogError("Could not find inspector window type!");
                return;
            }

            FieldInfo trackerField = inspectorWindowType.GetField("m_Tracker", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo propertyHandleCacheField = typeof(Editor).GetField("m_PropertyHandlerCache", BindingFlags.NonPublic | BindingFlags.Instance);

            if (trackerField == null || propertyHandleCacheField == null)
            {
                Debug.LogError("Could not find tracker field!");
                return;
            }

            //FieldInfo trackerEditorsField = trackerField.GetType().GetField("")

            Type propertyHandlerCacheType = FindTypeInAllAssemblies("UnityEditor.PropertyHandlerCache");

            if (propertyHandlerCacheType == null)
            {
                Debug.LogError("Could not find type of PropertyHandlerCache");
                return;
            }

            // Secondary nuke because Unity is great and keeps a cached copy of the events for every Editor in addition to a global cache we cleared earlier.
            EditorWindow[] editorWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();

            foreach (EditorWindow editor in editorWindows)
            {
                if (editor.GetType() == inspectorWindowType || editor.GetType().IsSubclassOf(inspectorWindowType))
                {
                    var activeEditorTracker = trackerField.GetValue(editor) as ActiveEditorTracker;

                    if (activeEditorTracker != null)
                        foreach (Editor activeEditor in activeEditorTracker.activeEditors)
                        {
                            if (activeEditor != null)
                            {
                                propertyHandleCacheField.SetValue(activeEditor, Activator.CreateInstance(propertyHandlerCacheType));
                                activeEditor.Repaint(); // Force repaint to get updated drawing of property
                            }
                        }
                }
            }
        }

        // Applies patch to Unity's builtin tracking for Drawers to redirect any drawers for Unity Events to our EasyEventDrawer instead.
        private static void ApplyEventDrawerPatch(bool enableOverride)
        {
            // Call here to find the scriptAttributeUtilityType in case it's needed for when overrides are disabled
            FieldInfo drawerTypeMap = GetDrawerTypeMap();

            if (enableOverride)
            {
                Type[] mapArgs = drawerTypeMap.FieldType.GetGenericArguments();

                Type keyType = mapArgs[0];
                Type valType = mapArgs[1];

                if (keyType == null || valType == null)
                {
                    Debug.LogError("Could not retrieve dictionary types!");
                    return;
                }

                FieldInfo drawerField = valType.GetField("drawer", BindingFlags.Public | BindingFlags.Instance);
                FieldInfo typeField = valType.GetField("type", BindingFlags.Public | BindingFlags.Instance);

                if (drawerField == null || typeField == null)
                {
                    Debug.LogError("Could not retrieve dictionary value fields!");
                    return;
                }

                var drawerTypeMapDict = drawerTypeMap.GetValue(null) as IDictionary;

                if (drawerTypeMapDict == null)
                {
                    MethodInfo popAttributesFunc = AttributeUtilityType.GetMethod("BuildDrawerTypeForTypeDictionary", BindingFlags.NonPublic | BindingFlags.Static);

                    if (popAttributesFunc == null)
                    {
                        Debug.LogError("Could not populate attributes for override!");
                        return;
                    }

                    popAttributesFunc.Invoke(null, new object[] { });

                    // Try again now that this should be populated
                    drawerTypeMapDict = drawerTypeMap.GetValue(null) as IDictionary;
                    if (drawerTypeMapDict == null)
                    {
                        Debug.LogError("Could not get dictionary for drawer types!");
                        return;
                    }
                }

                // Replace EventDrawer handles with our custom drawer
                var keysToRecreate = new List<object>();

                foreach (DictionaryEntry entry in drawerTypeMapDict)
                {
                    var drawerType = (Type) drawerField.GetValue(entry.Value);

                    if (drawerType.Name == "UnityEventDrawer" || drawerType.Name == "CollapsibleUnityEventDrawer") keysToRecreate.Add(entry.Key);
                }

                foreach (object keyToKill in keysToRecreate) { drawerTypeMapDict.Remove(keyToKill); }

                // Recreate these key-value pairs since they are structs
                foreach (object keyToRecreate in keysToRecreate)
                {
                    object newValMapping = Activator.CreateInstance(valType);
                    typeField.SetValue(newValMapping, (Type) keyToRecreate);
                    drawerField.SetValue(newValMapping, typeof(ReorderableUnityEventDrawer));

                    drawerTypeMapDict.Add(keyToRecreate, newValMapping);
                }
            }
            else
            {
                MethodInfo popAttributesFunc = AttributeUtilityType.GetMethod("BuildDrawerTypeForTypeDictionary", BindingFlags.NonPublic | BindingFlags.Static);

                if (popAttributesFunc == null)
                {
                    Debug.LogError("Could not populate attributes for override!");
                    return;
                }

                // Just force the editor to repopulate the drawers without nuking afterwards.
                popAttributesFunc.Invoke(null, new object[] { });
            }

            // Clear caches to force event drawers to refresh immediately.
            ClearPropertyCaches();
        }

        #endregion
    }

// #if UNITY_2018_3_OR_NEWER
//
//     // Use the new settings provider class instead so we don't need to add extra stuff to the Edit menu
//     // Using the IMGUI method
//     /// <summary>
//     ///     <see cref="SettingsProvider" /> for reorderable <see cref="UnityEvent" />s.
//     ///     Allows to configure <see cref="ReorderableUnityEventDrawer" /> project-wide through Project Settings window.
//     /// </summary>
//     public static class ReorderableUnityEventSettingsProvider
//     {
//         [SettingsProvider]
//         public static SettingsProvider CreateSettingsProvider()
//         {
//             var provider = new SettingsProvider("Project/Zinnia/Reorderable Unity Events", SettingsScope.Project)
//             {
//                 label = "Reorderable Unity Events",
//
//                 guiHandler = searchContext =>
//                 {
//                     ReorderableUnityEventHandler.ReorderableUnityEventSettings settings = ReorderableUnityEventHandler.GetEditorSettings();
//
//                     EditorGUI.BeginChangeCheck();
//                     ReorderableUnityEventSettingsGUIContent.DrawSettingsButtons(settings);
//
//                     if (EditorGUI.EndChangeCheck())
//                     {
//                         ReorderableUnityEventHandler.SetEditorSettings(settings);
//                         ReorderableUnityEventHandler.ApplyEventPropertyDrawerPatch(true);
//                     }
//                 },
//
//                 keywords = new HashSet<string>(new[] {"Zinnia", "Event", "UnityEvent", "Unity", "Reorderable"})
//             };
//
//             return provider;
//         }
//     }
//
//     // TODO: everything inside following #else block below can be removed if this codee is not going to be used in versions earlier than 2018.3.
//     //       As Zinnia supports only Unity 2018.3, removing it may make sense.
// #else
// public class ReorderableUnityEventSettings : EditorWindow
// {
//     [MenuItem("Edit/Easy Event Editor Settings")]
//     static void Init()
//     {
//         ReorderableUnityEventSettings window = GetWindow<ReorderableUnityEventSettings>(false, "EEE Settings");
//         window.minSize = new Vector2(350, 150);
//         window.maxSize = new Vector2(350, 150);
//         window.Show();
//     }
//
//     private void OnGUI()
//     {
//         EditorGUILayout.Space();
//         EditorGUILayout.LabelField("Easy Event Editor Settings", EditorStyles.boldLabel);
//
//         EditorGUILayout.Space();
//
//         ReorderableUnityEventHandler.EEESettings settings = ReorderableUnityEventHandler.GetEditorSettings();
//
//         EditorGUI.BeginChangeCheck();
//         SettingsGUIContent.DrawSettingsButtons(settings);
//
//         if (EditorGUI.EndChangeCheck())
//         {
//             ReorderableUnityEventHandler.SetEditorSettings(settings);
//             ReorderableUnityEventHandler.ApplyEventPropertyDrawerPatch(true);
//         }
//     }
// }
// #endif
//
//     /// <summary>
//     ///     Static class with <see cref="GUIContent" /> for the reorderable events settings window.
//     /// </summary>
//     internal static class ReorderableUnityEventSettingsGUIContent
//     {
//         private static readonly GUIContent EnableToggleGuiContent = new GUIContent("Enable Reorderable Unity Events", "Replaces the default Unity event editing context with reorderable one");
//
//         private static readonly GUIContent EnablePrivateMembersGuiContent =
//             new GUIContent("Show private properties and methods", "Exposes private/internal/obsolete properties and methods to the function list on events");
//
//         private static readonly GUIContent DisplayArgumentTypeContent = new GUIContent("Display argument type on function name", "Shows the argument that a function takes on the function header");
//
//         private static readonly GUIContent GroupSameComponentTypeContent = new GUIContent("Do not group components of the same type",
//             "If you have multiple components of the same type on one object, show all of them in listener function selection list. Unity hides duplicate components by default.");
//
//         private static readonly GUIContent UseHotkeys = new GUIContent("Use hotkeys",
//             "Adds common Unity hotkeys to event editor that operate on the currently selected event. The commands are Add (CTRL+A), Copy, Paste, Cut, Delete, and Duplicate");
//
//         public static void DrawSettingsButtons(ReorderableUnityEventHandler.ReorderableUnityEventSettings settings)
//         {
//             EditorGUILayout.Separator();
//
//             EditorGUI.indentLevel += 1;
//
//             settings.eventDrawerEnabled = EditorGUILayout.ToggleLeft(EnableToggleGuiContent, settings.eventDrawerEnabled);
//             EditorGUILayout.Separator();
//
//             EditorGUI.BeginDisabledGroup(!settings.eventDrawerEnabled);
//
//             settings.privateMembersShown = EditorGUILayout.ToggleLeft(EnablePrivateMembersGuiContent, settings.privateMembersShown);
//
//             settings.argumentTypeDisplayed = EditorGUILayout.ToggleLeft(DisplayArgumentTypeContent, settings.argumentTypeDisplayed);
//             settings.sameComponentTypesGrouped = !EditorGUILayout.ToggleLeft(GroupSameComponentTypeContent, !settings.sameComponentTypesGrouped);
//             settings.hotkeysEnabled = EditorGUILayout.ToggleLeft(UseHotkeys, settings.hotkeysEnabled);
//
//             EditorGUI.EndDisabledGroup();
//             EditorGUI.indentLevel -= 1;
//         }
//     }
}