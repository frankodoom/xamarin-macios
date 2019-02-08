using System;
using System.IO;
using System.Collections.Generic;
using BCLTestImporter;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using Xamarin.Utils;

namespace xharness.BCLTestImporter {
	// Class that is use as the connection between xharness and the BCLImporter
	// This class knows about both worlds, will get the required information, 
	// from xharness, that will then be passed to the importer to generate
	// the different projects, once those projects are generater, the factory
	// will return the iOS targets to be executed.
	public class BCLTestImportTargetFactory {
		public Harness Harness;
		BCLTestProjectGenerator projectGenerator;
		
		public BCLTestImportTargetFactory (Harness harness)
		{
			Harness = harness;
			var outputDir = Path.GetFullPath (Path.Combine (Harness.RootDirectory, "bcl-test", "BCLTests"));
			var projectTemplatePath = outputDir;
			var registerTypesTemplatePath = Path.Combine (outputDir, "RegisterType.cs.in");
			var plistTemplatePath = outputDir;

			projectGenerator = new BCLTestProjectGenerator (outputDir, Harness.MONO_PATH, projectTemplatePath, registerTypesTemplatePath, plistTemplatePath) {
				iOSMonoSDKPath = Harness.MONO_SDK_DESTDIR,
				Override = true,
			};
		}
		
		async Task<TestExecutingResult> RestoreNugetsAsync (string projectPath)
		{

			using (var nuget = new Process ()) {
				nuget.StartInfo.FileName = "/Library/Frameworks/Mono.framework/Versions/Current/Commands/nuget";
				var args = new StringBuilder ();
				args.Append ("restore ");
				args.Append (StringUtils.Quote (projectPath));
				nuget.StartInfo.Arguments = args.ToString ();

				var timeout = TimeSpan.FromMinutes (15);
				var result = await nuget.RunAsync (Harness.HarnessLog, true, timeout);
				if (result.TimedOut) {
					Harness.HarnessLog.WriteLine ("Nuget restore timed out after {0} seconds.", timeout.TotalSeconds);
					return TestExecutingResult.TimedOut;
				} 
				if (!result.Succeeded) {
					return TestExecutingResult.Failed;;
				}
				return TestExecutingResult.Succeeded;
			}
		}
		
		// generate all the different test targets.
		public List<iOSTestProject> GetiOSBclTargets ()
		{
			var result = new List<iOSTestProject> ();
			// generate all projects, then create a new iOSTarget per project
			foreach (var (name, path, xunit, platforms, failure) in projectGenerator.GenerateAlliOSTestProjects ()) {
				var prefix = xunit ? "xUnit" : "NUnit";
				result.Add (new iOSTestProject (path) {
					Name = $"[{prefix}] Mono {name}",
					SkiptvOSVariation = !platforms.Contains (Platform.TvOS),
					SkipwatchOSVariation = !platforms.Contains (Platform.WatchOS),
					FailureMessage = failure,
					Dependency = async () => {
						var nugetRestoreResult = await RestoreNugetsAsync (path);
						if (nugetRestoreResult != TestExecutingResult.Succeeded)
							throw new Exception ($"Nuget restore failed. {nugetRestoreResult}");
					}
				});
			}
			return result;
		}
		
		public List<MacTestProject> GetMacBclTargets (MacFlavors flavor)
		{
			Platform platform;
			if (flavor == MacFlavors.Full)
				platform = Platform.MacOSFull;
			else
				platform = Platform.MacOSModern;
			var result = new List<MacTestProject> ();
			foreach (var (name, path, xunit, failure) in projectGenerator.GenerateAllMacTestProjects (platform)) {
				var prefix = xunit ? "xUnit" : "NUnit";
				result.Add (new MacTestProject (path, targetFrameworkFlavor: flavor, generateVariations: false) {
					Name = $"[{prefix}] Mono {name}",
					Platform = "AnyCPU",
					IsExecutableProject = true,
					FailureMessage = failure,
					Dependency = async () => {
						var rv = await Harness.BuildBclTests ();
						if (!rv.Succeeded)
							throw new Exception ($"Failed to build BCL tests, exit code: {rv.ExitCode}. Check the harness log for more details.");

						var nugetRestoreResult = await RestoreNugetsAsync (path);
						if (nugetRestoreResult != TestExecutingResult.Succeeded)
							throw new Exception ($"Nuget restore failed. {nugetRestoreResult}");
					}
				});
			}
			return result;
		}

		public List<MacTestProject> GetMacBclTargets ()
		{
			var result = new List<MacTestProject> ();
			foreach (var flavor in new [] { MacFlavors.Full, MacFlavors.Modern}) {
				result.AddRange (GetMacBclTargets (flavor));
			}
			return result;
		}
	}
}

