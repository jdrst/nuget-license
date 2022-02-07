﻿using NuGet.Protocol.Core.Types;
using NuGetUtility.Extensions;
using NuGetUtility.Wrapper.MsBuildWrapper;
using NuGetUtility.Wrapper.NuGetWrapper.Packaging.Core;
using NuGetUtility.Wrapper.NuGetWrapper.ProjectModel;
using NuGetUtility.Wrapper.NuGetWrapper.Protocol.Core.Types;
using NuGetUtility.Wrapper.NuGetWrapper.Versioning;

namespace NuGetUtility.ReferencedPackagesReader
{
    internal class ReferencedPackageReader
    {
        private const string ProjectReferenceIdentifier = "project";
        private readonly IEnumerable<string> _ignoredPackages;
        private readonly ILockFileFactory _lockFileFactory;
        private readonly IPackageSearchMetadataBuilderFactory _metadataBuilderFactory;
        private readonly IMsBuildAbstraction _msBuild;

        public ReferencedPackageReader(IEnumerable<string> ignoredPackages, IMsBuildAbstraction msBuild,
            ILockFileFactory lockFileFactory, IPackageSearchMetadataBuilderFactory metadataBuilderFactory)
        {
            _ignoredPackages = ignoredPackages;
            _msBuild = msBuild;
            _lockFileFactory = lockFileFactory;
            _metadataBuilderFactory = metadataBuilderFactory;
        }

        public IEnumerable<IPackageSearchMetadata> GetInstalledPackages(string projectPath, bool includeTransitive)
        {
            var project = _msBuild.GetProject(projectPath);

            var assetsFile = LoadAssetsFile(projectPath, project);

            var referencedLibraries = new HashSet<ILockFileLibrary>();

            foreach (var target in assetsFile.Targets!)
            {
                var referencedLibrariesForTarget =
                    assetsFile.Libraries.Where(l => l.Type != ProjectReferenceIdentifier);

                if (!includeTransitive)
                {
                    var targetFrameworkInformation = GetTargetFrameworkInformation(target, assetsFile);
                    var directlyReferencedPackages =
                        _msBuild.GetPackageReferencesFromProjectForFramework(projectPath,
                            targetFrameworkInformation.ToString()!);

                    referencedLibrariesForTarget =
                        referencedLibrariesForTarget.Where(l => IsDirectlyReferenced(l, directlyReferencedPackages));
                }

                referencedLibraries.AddRange(referencedLibrariesForTarget);
            }

            return referencedLibraries.Where(IsNotIgnoredPackage).Select(r =>
                _metadataBuilderFactory.FromIdentity(new PackageIdentity(r.Name, r.Version)).Build());
        }

        private bool IsNotIgnoredPackage(ILockFileLibrary packageInfo)
        {
            return !_ignoredPackages.Any(p => p.Equals(packageInfo.Name));
        }

        private bool IsDirectlyReferenced(ILockFileLibrary library,
            IEnumerable<PackageReference> directlyReferencedPackages)
        {
            return directlyReferencedPackages.Any(p =>
                library.Name.Equals(p.PackageName, StringComparison.OrdinalIgnoreCase) && ((p.Version == null) ||
                    library.Version.Equals(new WrappedNuGetVersion(p.Version))));
        }

        private static ITargetFrameworkInformation GetTargetFrameworkInformation(ILockFileTarget target,
            ILockFile assetsFile)
        {
            try
            {
                return assetsFile.PackageSpec.TargetFrameworks.First(
                    t => t.FrameworkName.Equals(target.TargetFramework));
            }
            catch (Exception e)
            {
                throw new ReferencedPackageReaderException(
                    $"Failed to identify the target framework information for {target}", e);
            }
        }

        private ILockFile LoadAssetsFile(string projectPath, IProject project)
        {
            var assetsPath = project.GetAssetsPath();
            var assetsFile = _lockFileFactory.GetFromFile(assetsPath);

            if (!assetsFile.PackageSpec.IsValid() || (assetsFile.Targets == null) || !assetsFile.Targets.Any())
            {
                throw new ReferencedPackageReaderException(
                    $"Failed to validate project assets for project {projectPath}");
            }

            return assetsFile;
        }
    }
}
