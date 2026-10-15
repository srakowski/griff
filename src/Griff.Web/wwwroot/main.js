// Starts .NET with the web player (Griff.Web), gives it the page (griff.js, as the "griff" module its [JSImport]s
// name), and runs its Main, which fetches the game and starts it. The loading screen's bar shows .NET's files coming
// (most of it), then the game's.
import createDotnetRuntime from './_framework/dotnet.js';
import * as griff from './griff.js';

try {
  const { setModuleImports, getAssemblyExports, getConfig, runMain } = await createDotnetRuntime({
    onDownloadResourceProgress: (loaded, total) => griff.loading(loaded, total),
  });
  setModuleImports('griff', griff);
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  griff.attach(exports.Griff.Web.WebPlayer);
  await runMain();
} catch (error) {
  griff.fail(`Couldn't start .NET: ${error?.message ?? error}`);
}
