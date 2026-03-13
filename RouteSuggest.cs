using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;

namespace RouteSuggest;

[ModInitializer("ModLoaded")]
public static class RouteSuggest {
	public static void ModLoaded() {
		Log.Warn("RouteSuggest Loaded");
	}
}
