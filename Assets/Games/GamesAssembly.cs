// Games are found by reflection (see GameRegistry), so nothing references them directly.
// This keeps the assembly in Android builds; Assets/link.xml keeps every type inside it.
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
