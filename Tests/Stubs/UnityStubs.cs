// Clean-room stand-ins for the Unity types game code touches on the paths the conformance tests exercise.
// Empty shapes only — no Unity code, no Unity IP; just enough for type loading and initializers to succeed.
//
// EXERCISE-DRIVEN, NOT AN API SURFACE: every member here exists because a specific headless run demanded it
// (Log's initializer wanted Application's events and isEditor; XmlFile's ctor overloads wanted TextAsset;
// Assembly-CSharp's type scan wanted the MonoBehaviour hierarchy). Expect to add more as tests reach further
// into game code — each addition is two lines and a rerun. Do NOT try to mirror Unity's real API.
//
// Shapes must match Unity's exactly where the runtime checks them: LogCallback is nested inside Application
// (a namespace-level delegate produced "Could not load type 'LogCallback'"), and the inheritance chain
// Object -> Component -> Behaviour -> MonoBehaviour must hold or thousands of game types fail to load.

namespace UnityEngine {
  public enum LogType { Error = 0, Assert = 1, Warning = 2, Log = 3, Exception = 4 }

  public enum RuntimePlatform { WindowsPlayer = 2, WindowsEditor = 7, LinuxPlayer = 13, LinuxEditor = 16 }

  public class Application {
    public delegate void LogCallback(string condition, string stackTrace, LogType type);

    public static event LogCallback logMessageReceivedThreaded { add { } remove { } }
    public static event LogCallback logMessageReceived { add { } remove { } }
    public static bool isEditor => false;
    public static bool isBatchMode => true;
    public static bool isPlaying => false;
    public static RuntimePlatform platform => RuntimePlatform.WindowsPlayer;
    public static string unityVersion => "0.0.0-headless-stub";
    public static string consoleLogPath => "";
    public static string dataPath => "";
    public static string persistentDataPath => "";
  }

  public class Object {
    public string name { get; set; } = "";
  }

  public class Component : Object { }

  public class Behaviour : Component { }

  public class MonoBehaviour : Behaviour { }

  public class ScriptableObject : Object { }

  public class GameObject : Object { }

  public class Transform : Component { }

  public class TextAsset : Object {
    public string text => "";
  }

  public class StackTraceUtility {
    public static string ExtractStackTrace() => "";
    public static string ExtractStringFromException(object exception) => exception?.ToString() ?? "";
  }

  public class Debug {
    public static void Log(object message) { }
    public static void LogWarning(object message) { }
    public static void LogError(object message) { }
    public static void LogException(System.Exception exception) { }
  }
}

namespace UnityEngine.Scripting {
  public class PreserveAttribute : System.Attribute { }
}
