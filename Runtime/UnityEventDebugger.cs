namespace EE.TalTech.IVAR.AdvancedUnityEventInspector.Components
{
    using System;
    using System.Diagnostics;
    using UnityEngine;
    using Debug = UnityEngine.Debug;

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

        public void LogString(string message) { Log(message); }

        public void LogBool(bool value) { Log(value); }

        public void LogFloat(float value) { Log(value); }

        public void LogVector2(Vector2 value) { Log(value); }

        public void LogVector3(Vector3 value) { Log(value); }

        public void LogPose(Pose value) { Log($"{value.position.ToString("F3")}{value.rotation.eulerAngles.ToString("F3")}"); }

        public void LogObject(GameObject gameObject) { Log(gameObject.ToString()); }

        public void LogComponent(Component component) { Log(component.ToString()); }

        public void LogFrameAndTime() { Log($"Frame {Time.frameCount} ({Time.realtimeSinceStartup:n2} s since startup)"); }

        public void ThrowNotImplemented(string message) { throw new NotImplementedException(message); }
        
        private void Log(object message)
        {
            if (!enabled) return;

            Debug.Log($"{message}\nSource: '{GetCallerName(5)}'", this);
        }

        private static string GetCallerName(int level = 2)
        {
            var m = new StackTrace().GetFrame(level).GetMethod();

            // .Name is the name only, .FullName includes the namespace
            string className = m.ReflectedType?.FullName;

            //the method/function name you are looking for.
            string methodName = m.Name;

            //returns a composite of the namespace, class and method name.
            return $"{className}.{methodName}";
        }
    }
}