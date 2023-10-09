using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Anatawa12.AvatarOptimizer.ErrorReporting
{
    [Serializable]
    internal class AvatarReport
    {
        [SerializeField] internal ObjectRef objectRef;
        [SerializeField] internal bool successful;
        [SerializeField] internal List<ErrorLog> logs = new List<ErrorLog>();
    }

    [InitializeOnLoad]
    [Serializable]
    public class BuildReport
    {
        private const string Path = "Library/com.anatawa12.error-reporting.json";

        private static BuildReport _report;
        private Stack<Object> _references = new Stack<Object>();

        [SerializeField] internal List<AvatarReport> avatars = new List<AvatarReport>();

        internal ConditionalWeakTable<Transform, AvatarReport> AvatarsByObject =
            new ConditionalWeakTable<Transform, AvatarReport>();
        internal AvatarReport CurrentAvatar { get; set; }

        internal static BuildReport CurrentReport
        {
            get
            {
                if (_report == null) _report = LoadReport() ?? new BuildReport();
                return _report;
            }
        }

        static BuildReport()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                switch (change)
                {
                    case PlayModeStateChange.ExitingEditMode:
                        // TODO - skip if we're doing a VRCSDK build
                        _report = new BuildReport();
                        break;
                }
            };
        }

        private static BuildReport LoadReport()
        {
            try
            {
                var data = File.ReadAllText(Path);
                return JsonUtility.FromJson<BuildReport>(data);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static void SaveReport()
        {
            var report = CurrentReport;
            var json = JsonUtility.ToJson(report);

            File.WriteAllText(Path, json);

            ErrorReportUI.ReloadErrorReport();
        }

        internal AvatarReport Initialize([NotNull] Transform descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            AvatarReport report = new AvatarReport();
            report.objectRef = new ObjectRef(descriptor.gameObject);
            avatars.Add(report);
            report.successful = true;

            report.logs.AddRange(ComponentValidation.ValidateAll(descriptor.gameObject));

            AvatarsByObject.Add(descriptor, report);
            return report;
        }

        [CanBeNull]
        internal static ErrorLog Log(ReportLevel level, string code, string[] strings, Assembly assembly)
        {
            for (var i = 0; i < strings.Length; i++)
                strings[i] = strings[i] ?? "";
            var errorLog = new ErrorLog(level, code, strings, assembly);

            var avatarReport = CurrentReport.CurrentAvatar;
            if (avatarReport == null)
            {
                Debug.LogWarning("Error logged when not processing an avatar: " + errorLog);
                return null;
            }

            avatarReport.logs.Add(errorLog);
            return errorLog;
        }

        [CanBeNull]
        public static ErrorLog LogInfo(string code, params string[] strings) => Log(ReportLevel.Info, code,
            strings: strings, assembly: Assembly.GetCallingAssembly());

        [CanBeNull]
        public static ErrorLog LogWarning(string code, params string[] strings) => Log(ReportLevel.Warning, code,
            strings: strings, assembly: Assembly.GetCallingAssembly());

        [CanBeNull]
        public static ErrorLog LogFatal(string code, params string[] strings)
        {
            var log = Log(ReportLevel.Error, code, strings: strings, assembly: Assembly.GetCallingAssembly());
            if (CurrentReport.CurrentAvatar != null)
            {
                CurrentReport.CurrentAvatar.successful = false;
            }
            else
            {
                throw new Exception("Fatal error without error reporting scope");
            }
            return log;
        }

        internal static void LogException(Exception e, string additionalStackTrace = "")
        {
            var avatarReport = CurrentReport.CurrentAvatar;
            if (avatarReport == null)
            {
                Debug.LogException(e);
                return;
            }
            else
            {
                avatarReport.logs.Add(new ErrorLog(e, additionalStackTrace));
            }
        }

        public static T ReportingObject<T>(Object obj, Func<T> action) => ReportingObject(obj, false, action);

        public static T ReportingObject<T>(Object obj, bool needThrow, Func<T> action)
        {
            if (obj != null) CurrentReport._references.Push(obj);
            try
            {
                return action();
            }
            catch (ReportedException)
            {
                throw; // rethrow only
            }
            catch (Exception e)
            {
                // just rethrow if BuildReport is not in progress
                if (CurrentReport.CurrentAvatar == null && needThrow) throw;
                ReportInternalError(e, 2);
                if (needThrow) throw new ReportedException();
                return default;
            }
            finally
            {
                if (obj != null) CurrentReport._references.Pop();
            }
        }

        public static void ReportInternalError(Exception exception) => ReportInternalError(exception, 2);

        private static void ReportInternalError(Exception exception, int strips)
        {
            if (exception is ReportedException) return; // reported exception is known internal error
            var additionalStackTrace = string.Join("\n", 
                Environment.StackTrace.Split('\n').Skip(strips)) + "\n";
            LogException(exception, additionalStackTrace);
        }

        public static void ReportingObject(Object obj, Action action) => ReportingObject(obj, false, action);

        public static void ReportingObject(Object obj, bool needThrow, Action action)
        {
            ReportingObject(obj, needThrow, () =>
            {
                action();
                return true;
            });
        }

        public static void ReportingObjects<T>(IEnumerable<T> objs, Action<T> action) where T : Object
        {
            foreach (var obj in objs)
                ReportingObject(obj, () => action(obj));
        }

        internal IEnumerable<ObjectRef> GetActiveReferences()
        {
            return _references.Select(o => new ObjectRef(o));
        }

        public static void Clear()
        {
            _report = new BuildReport();
        }

        public static void RemapPaths(string original, string cloned)
        {
            foreach (var av in CurrentReport.avatars)
            {
                av.objectRef = av.objectRef.Remap(original, cloned);

                foreach (var log in av.logs)
                {
                    log.referencedObjects = log.referencedObjects.Select(o => o.Remap(original, cloned)).ToList();
                }
            }

            ErrorReportUI.ReloadErrorReport();
        }

        private class ReportedException : Exception
        {
        }
    }
}
