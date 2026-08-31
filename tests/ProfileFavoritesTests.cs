using System;
using System.Collections.Generic;
using LlamaServerLauncher.Models;

public static class ProfileFavoritesTests
{
    private static string Order(params string[] names) =>
        string.Join(",", ProfileFavorites.Order(names));

    public static void Run(Harness h)
    {
        h.Section("Profile favorites");

        ProfileFavorites.Clear();
        h.Check("clean state has no favorites", !ProfileFavorites.IsFavorite("Qwen"), "ok");
        h.Check("without favorites the order is untouched",
            Order("Aya", "Qwen", "Gemma") == "Aya,Qwen,Gemma", Order("Aya", "Qwen", "Gemma"));

        ProfileFavorites.Set(new List<string> { "Qwen", "Gemma" });
        h.Check("favorites come first, both groups keep their order",
            Order("Aya", "Gemma", "Mistral", "Qwen") == "Gemma,Qwen,Aya,Mistral",
            Order("Aya", "Gemma", "Mistral", "Qwen"));
        h.Check("lookup ignores case", ProfileFavorites.IsFavorite("qwen"), "ok");
        h.Check("the first non-favorite marks the boundary",
            ProfileFavorites.IsFirstAfterFavorites("Aya"), "ok");
        h.Check("the rest of the list is not the boundary",
            !ProfileFavorites.IsFirstAfterFavorites("Mistral"), "ok");

        Order("Qwen", "Gemma");
        h.Check("a list of favorites alone has no boundary",
            !ProfileFavorites.IsFirstAfterFavorites("Qwen"), "ok");
        Order("Aya", "Mistral");
        h.Check("a list without favorites has no boundary",
            !ProfileFavorites.IsFirstAfterFavorites("Aya"), "ok");

        h.Check("a name missing from the list is dropped from the order",
            Order("Aya", "Qwen") == "Qwen,Aya", Order("Aya", "Qwen"));

        h.Check("toggle pins a profile", ProfileFavorites.Toggle("Aya"), "ok");
        h.Check("toggle unpins it again", !ProfileFavorites.Toggle("aya"), "ok");
        h.Check("unpinned profile is no longer a favorite", !ProfileFavorites.IsFavorite("Aya"), "ok");
        h.Check("an empty name is never pinned", !ProfileFavorites.Toggle("  "), "ok");

        ProfileFavorites.Rename("Qwen", "Qwen3");
        h.Check("rename carries the flag over", ProfileFavorites.IsFavorite("Qwen3"), "ok");
        h.Check("the old name is gone", !ProfileFavorites.IsFavorite("Qwen"), "ok");
        ProfileFavorites.Rename("Mistral", "Mistral2");
        h.Check("renaming a plain profile pins nothing", !ProfileFavorites.IsFavorite("Mistral2"), "ok");

        ProfileFavorites.Remove("gemma");
        h.Check("remove drops the flag ignoring case", !ProfileFavorites.IsFavorite("Gemma"), "ok");
        h.Check("saved names are sorted",
            string.Join(",", ProfileFavorites.Names) == "Qwen3", string.Join(",", ProfileFavorites.Names));

        ProfileFavorites.Set(null);
        h.Check("no stored favorites means none are set", !ProfileFavorites.IsFavorite("Qwen3"), "ok");

        ProfileFavorites.Clear();
    }
}
