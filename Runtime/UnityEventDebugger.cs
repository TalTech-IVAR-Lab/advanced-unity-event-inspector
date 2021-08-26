namespace Games.NoSoySauce.DeveloperTools.Components
{
    using System;
    using System.Diagnostics;
    using UnityEngine;
    using Malimbe.BehaviourStateRequirementMethod;
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Provides simple methods which can be hooked up to Unity events for quick debugging in Editor.
    /// </summary>
    [ExecuteAlways]
    public class UnityEventDebugger : MonoBehaviour
    {
        private void OnEnable()
        {
            // Here just to make component toggleable.
        }

        [RequiresBehaviourState]
        public void LogString(string message)
        {
            Log(message, this);
        }

        [RequiresBehaviourState]
        public void LogString(string message, Object context)
        {
            Log(message, context);
        }

        [RequiresBehaviourState]
        public void LogBool(bool value)
        {
            Log(value, this);
        }

        [RequiresBehaviourState]
        public void LogFloat(float value)
        {
            Log(value, this);
        }

        [RequiresBehaviourState]
        public void LogVector2(Vector2 value)
        {
            Log(value, this);
        }

        [RequiresBehaviourState]
        public void LogVector3(Vector3 value)
        {
            Log(value, this);
        }

        [RequiresBehaviourState]
        public void LogPose(Pose value)
        {
            Log($"{value.position.ToString("F3")}{value.rotation.eulerAngles.ToString("F3")}", this);
        }

        [RequiresBehaviourState]
        public void LogObject(GameObject gameObject)
        {
            Log(gameObject.ToString(), this);
        }

        [RequiresBehaviourState]
        public void LogComponent(Component component)
        {
            Log(component.ToString(), this);
        }

        [RequiresBehaviourState]
        public void LogFrameAndTime()
        {
            Log($"Frame {Time.frameCount} ({Time.realtimeSinceStartup:n2} s since startup)", this);
        }

        private static void Log(object message, Object context)
        {
            Debug.Log($"{message}\nSource: '{GetCallerName(5)}'", context);
        }
        
        private static string GetCallerName(int level = 2)
        {
            var m = new StackTrace().GetFrame(level).GetMethod();

            // .Name is the name only, .FullName includes the namespace
            var className = m.ReflectedType?.FullName;

            //the method/function name you are looking for.
            var methodName = m.Name;

            //returns a composite of the namespace, class and method name.
            return $"{className}.{methodName}";
        }
    }
}