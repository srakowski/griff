using Griff.Runtime;
using Griff.Web;

// The web player: fetches the game the page sits next to into the browser's in-memory file system, loads it as the
// desktop player does, and hands it to WebPlayer, which sets up its sound and saves and the page then runs frame by frame.
try
{
    var loaded = await WebPlayer.FetchGameAsync(new Uri(Browser.BaseUrl()));
    await WebPlayer.RunAsync(loaded);
}
#pragma warning disable CA1031 // Whatever stops the game loading is shown to the player rather than lost in the console.
catch (Exception ex)
#pragma warning restore CA1031
{
    Console.Error.WriteLine(ex);
    Browser.Fail($"Couldn't load the game: {ex.Message}");
}
