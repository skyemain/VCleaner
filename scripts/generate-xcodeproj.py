#!/usr/bin/env python3
"""Regenerate Clenzy.xcodeproj from the Swift sources. Run from the branch root."""

import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def uid(text: str) -> str:
    return hashlib.sha1(text.encode()).hexdigest()[:24].upper()


def main() -> None:
    swift_app = sorted((ROOT / "Clenzy").rglob("*.swift"))
    swift_tests = sorted((ROOT / "ClenzyTests").rglob("*.swift"))
    parents = {Path("Clenzy"), Path("Clenzy/Resources")}
    for path in swift_app:
        relative = path.relative_to(ROOT)
        current = relative.parent
        while current != Path("."):
            parents.add(current)
            current = current.parent

    def file_ref(path: Path) -> str:
        return uid("fileref:" + path.as_posix())

    def build_ref(path: Path) -> str:
        return uid("build:" + path.as_posix())

    group_id = {directory: uid("group:" + directory.as_posix()) for directory in parents}
    test_group = uid("group:tests")
    main_group = uid("group:main")
    products_group = uid("group:products")
    app_target = uid("target:app")
    test_target = uid("target:tests")
    project_id = uid("project")
    app_product = uid("product:app")
    test_product = uid("product:tests")
    objects: list[str] = []

    def add(identifier: str, body: str) -> None:
        objects.append(f"\t\t{identifier} = {body};")

    for path in swift_app + swift_tests:
        relative = path.relative_to(ROOT)
        add(file_ref(relative), "{ isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = %s; sourceTree = \"<group>\"; }" % path.name)
    assets = Path("Clenzy/Resources/Assets.xcassets")
    add(file_ref(assets), '{ isa = PBXFileReference; lastKnownFileType = folder.assetcatalog; path = Assets.xcassets; sourceTree = "<group>"; }')
    add(app_product, '{ isa = PBXFileReference; explicitFileType = wrapper.application; includeInIndex = 0; path = Clenzy.app; sourceTree = BUILT_PRODUCTS_DIR; }')
    add(test_product, '{ isa = PBXFileReference; explicitFileType = wrapper.cfbundle; includeInIndex = 0; path = ClenzyTests.xctest; sourceTree = BUILT_PRODUCTS_DIR; }')

    for directory in sorted(parents, key=lambda item: (len(item.parts), item.as_posix())):
        children = [group_id[child] for child in sorted(parents) if child.parent == directory]
        children.extend(file_ref(path.relative_to(ROOT)) for path in swift_app if path.relative_to(ROOT).parent == directory)
        if directory == Path("Clenzy/Resources"):
            children.append(file_ref(assets))
        body = ", ".join(children) if children else ""
        add(group_id[directory], '{ isa = PBXGroup; children = ( %s ); path = %s; sourceTree = "<group>"; }' % (body, directory.name))

    test_children = ", ".join(file_ref(path.relative_to(ROOT)) for path in swift_tests)
    add(test_group, '{ isa = PBXGroup; children = ( %s ); path = ClenzyTests; sourceTree = "<group>"; }' % test_children)
    add(products_group, '{ isa = PBXGroup; children = ( %s, %s ); name = Products; sourceTree = "<group>"; }' % (app_product, test_product))
    add(main_group, '{ isa = PBXGroup; children = ( %s, %s, %s ); sourceTree = "<group>"; }' % (group_id[Path("Clenzy")], test_group, products_group))

    for path in swift_app + swift_tests:
        relative = path.relative_to(ROOT)
        add(build_ref(relative), "{ isa = PBXBuildFile; fileRef = %s; }" % file_ref(relative))
    add(build_ref(assets), "{ isa = PBXBuildFile; fileRef = %s; }" % file_ref(assets))

    sources = uid("phase:sources")
    resources = uid("phase:resources")
    frameworks = uid("phase:frameworks")
    test_sources = uid("phase:testsources")
    test_frameworks = uid("phase:testframeworks")
    add(sources, "{ isa = PBXSourcesBuildPhase; buildActionMask = 2147483647; files = ( %s ); runOnlyForDeploymentPostprocessing = 0; }" % ", ".join(build_ref(path.relative_to(ROOT)) for path in swift_app))
    add(resources, "{ isa = PBXResourcesBuildPhase; buildActionMask = 2147483647; files = ( %s ); runOnlyForDeploymentPostprocessing = 0; }" % build_ref(assets))
    add(frameworks, "{ isa = PBXFrameworksBuildPhase; buildActionMask = 2147483647; files = ( ); runOnlyForDeploymentPostprocessing = 0; }")
    add(test_sources, "{ isa = PBXSourcesBuildPhase; buildActionMask = 2147483647; files = ( %s ); runOnlyForDeploymentPostprocessing = 0; }" % ", ".join(build_ref(path.relative_to(ROOT)) for path in swift_tests))
    add(test_frameworks, "{ isa = PBXFrameworksBuildPhase; buildActionMask = 2147483647; files = ( ); runOnlyForDeploymentPostprocessing = 0; }")

    app_settings = """
				TARGETED_DEVICE_FAMILY = "1,2";
				SUPPORTED_PLATFORMS = "iphoneos iphonesimulator";
				CODE_SIGN_STYLE = Automatic;
				SUPPORTS_MACCATALYST = NO;
				CURRENT_PROJECT_VERSION = 1;
				
				GENERATE_INFOPLIST_FILE = NO;
				INFOPLIST_FILE = Clenzy/Resources/Info.plist;
				LD_RUNPATH_SEARCH_PATHS = "$(inherited) @executable_path/Frameworks";
				MARKETING_VERSION = 1.0;
				PRODUCT_BUNDLE_IDENTIFIER = com.skyemain.Clenzy.ios;
				PRODUCT_NAME = Clenzy;
				SWIFT_STRICT_CONCURRENCY = minimal;
				SWIFT_VERSION = 5.0;"""
    test_settings = """
				BUNDLE_LOADER = "$(TEST_HOST)";
				CODE_SIGNING_ALLOWED = NO;
				GENERATE_INFOPLIST_FILE = YES;
				IPHONEOS_DEPLOYMENT_TARGET = 17.0;
				PRODUCT_BUNDLE_IDENTIFIER = com.skyemain.Clenzy.iosTests;
				PRODUCT_NAME = "$(TARGET_NAME)";
				SWIFT_STRICT_CONCURRENCY = minimal;
				SWIFT_VERSION = 5.0;
				TEST_HOST = "$(BUILT_PRODUCTS_DIR)/Clenzy.app/Clenzy";"""
    project_settings = """
				ALWAYS_SEARCH_USER_PATHS = NO;
				CLANG_ENABLE_OBJC_ARC = YES;
				IPHONEOS_DEPLOYMENT_TARGET = 17.0;
				SDKROOT = iphoneos;
				SWIFT_VERSION = 5.0;"""

    debug_app, release_app = uid("cfg:app:debug"), uid("cfg:app:release")
    debug_test, release_test = uid("cfg:test:debug"), uid("cfg:test:release")
    debug_proj, release_proj = uid("cfg:proj:debug"), uid("cfg:proj:release")
    add(debug_app, "{ isa = XCBuildConfiguration; buildSettings = { %s\n\t\t\t\tENABLE_TESTABILITY = YES;\n\t\t\t\tCODE_SIGN_IDENTITY = \"-\";\n\t\t\t}; name = Debug; }" % app_settings)
    add(release_app, "{ isa = XCBuildConfiguration; buildSettings = { %s\n\t\t\t}; name = Release; }" % app_settings)
    add(debug_test, "{ isa = XCBuildConfiguration; buildSettings = { %s\n\t\t\t}; name = Debug; }" % test_settings)
    add(release_test, "{ isa = XCBuildConfiguration; buildSettings = { %s\n\t\t\t}; name = Release; }" % test_settings)
    add(debug_proj, "{ isa = XCBuildConfiguration; buildSettings = { %s\n\t\t\t\tONLY_ACTIVE_ARCH = YES;\n\t\t\t}; name = Debug; }" % project_settings)
    add(release_proj, "{ isa = XCBuildConfiguration; buildSettings = { %s\n\t\t\t\tONLY_ACTIVE_ARCH = NO;\n\t\t\t\tSWIFT_COMPILATION_MODE = wholemodule;\n\t\t\t}; name = Release; }" % project_settings)

    app_list, test_list, proj_list = uid("list:app"), uid("list:test"), uid("list:proj")
    add(app_list, "{ isa = XCConfigurationList; buildConfigurations = ( %s, %s ); defaultConfigurationIsVisible = 0; defaultConfigurationName = Release; }" % (debug_app, release_app))
    add(test_list, "{ isa = XCConfigurationList; buildConfigurations = ( %s, %s ); defaultConfigurationIsVisible = 0; defaultConfigurationName = Release; }" % (debug_test, release_test))
    add(proj_list, "{ isa = XCConfigurationList; buildConfigurations = ( %s, %s ); defaultConfigurationIsVisible = 0; defaultConfigurationName = Release; }" % (debug_proj, release_proj))

    add(app_target, """{ isa = PBXNativeTarget; buildConfigurationList = %s; buildPhases = ( %s, %s, %s ); buildRules = ( ); dependencies = ( ); name = Clenzy; productName = Clenzy; productReference = %s; productType = "com.apple.product-type.application"; }""" % (app_list, sources, frameworks, resources, app_product))
    proxy, dependency = uid("proxy"), uid("dependency")
    add(proxy, "{ isa = PBXContainerItemProxy; containerPortal = %s; proxyType = 1; remoteGlobalIDString = %s; remoteInfo = Clenzy; }" % (project_id, app_target))
    add(dependency, "{ isa = PBXTargetDependency; target = %s; targetProxy = %s; }" % (app_target, proxy))
    add(test_target, """{ isa = PBXNativeTarget; buildConfigurationList = %s; buildPhases = ( %s, %s ); buildRules = ( ); dependencies = ( %s ); name = ClenzyTests; productName = ClenzyTests; productReference = %s; productType = "com.apple.product-type.bundle.unit-test"; }""" % (test_list, test_sources, test_frameworks, dependency, test_product))
    add(project_id, """{ isa = PBXProject; attributes = { BuildIndependentTargetsInParallel = 1; LastUpgradeCheck = 1500; }; buildConfigurationList = %s; compatibilityVersion = "Xcode 14.0"; developmentRegion = en; hasScannedForEncodings = 0; knownRegions = ( en, Base ); mainGroup = %s; productRefGroup = %s; projectDirPath = ""; projectRoot = ""; targets = ( %s, %s ); }""" % (proj_list, main_group, products_group, app_target, test_target))

    project = ROOT / "Clenzy.xcodeproj"
    project.mkdir(exist_ok=True)
    (project / "project.pbxproj").write_text(
        "// !$*UTF8*$!\n{\n\tarchiveVersion = 1;\n\tclasses = {\n\t};\n\tobjectVersion = 56;\n\tobjects = {\n"
        + "\n".join(line.rstrip() for obj in objects for line in obj.splitlines())
        + "\n\t};\n\trootObject = %s;\n}\n" % project_id
    )
    scheme_dir = project / "xcshareddata" / "xcschemes"
    scheme_dir.mkdir(parents=True, exist_ok=True)
    (scheme_dir / "Clenzy.xcscheme").write_text(f"""<?xml version="1.0" encoding="UTF-8"?>
<Scheme LastUpgradeVersion="1500" version="1.7">
  <BuildAction parallelizeBuildables="YES" buildImplicitDependencies="YES">
    <BuildActionEntries>
      <BuildActionEntry buildForTesting="YES" buildForRunning="YES" buildForProfiling="YES" buildForArchiving="YES" buildForAnalyzing="YES">
        <BuildableReference BuildableIdentifier="primary" BlueprintIdentifier="{app_target}" BuildableName="Clenzy.app" BlueprintName="Clenzy" ReferencedContainer="container:Clenzy.xcodeproj"/>
      </BuildActionEntry>
    </BuildActionEntries>
  </BuildAction>
  <TestAction buildConfiguration="Debug" selectedDebuggerIdentifier="Xcode.DebuggerFoundation.Debugger.LLDB" selectedLauncherIdentifier="Xcode.DebuggerFoundation.Launcher.LLDB" shouldUseLaunchSchemeArgsEnv="YES">
    <Testables>
      <TestableReference skipped="NO">
        <BuildableReference BuildableIdentifier="primary" BlueprintIdentifier="{test_target}" BuildableName="ClenzyTests.xctest" BlueprintName="ClenzyTests" ReferencedContainer="container:Clenzy.xcodeproj"/>
      </TestableReference>
    </Testables>
  </TestAction>
  <LaunchAction buildConfiguration="Debug" selectedDebuggerIdentifier="Xcode.DebuggerFoundation.Debugger.LLDB" selectedLauncherIdentifier="Xcode.DebuggerFoundation.Launcher.LLDB" launchStyle="0" useCustomWorkingDirectory="NO" ignoresPersistentStateOnLaunch="NO" debugDocumentVersioning="YES" debugServiceExtension="internal" allowLocationSimulation="YES">
    <BuildableProductRunnable runnableDebuggingMode="0">
      <BuildableReference BuildableIdentifier="primary" BlueprintIdentifier="{app_target}" BuildableName="Clenzy.app" BlueprintName="Clenzy" ReferencedContainer="container:Clenzy.xcodeproj"/>
    </BuildableProductRunnable>
  </LaunchAction>
  <ProfileAction buildConfiguration="Release" shouldUseLaunchSchemeArgsEnv="YES" savedToolIdentifier="" useCustomWorkingDirectory="NO" debugDocumentVersioning="YES">
    <BuildableProductRunnable runnableDebuggingMode="0">
      <BuildableReference BuildableIdentifier="primary" BlueprintIdentifier="{app_target}" BuildableName="Clenzy.app" BlueprintName="Clenzy" ReferencedContainer="container:Clenzy.xcodeproj"/>
    </BuildableProductRunnable>
  </ProfileAction>
  <AnalyzeAction buildConfiguration="Debug"/>
  <ArchiveAction buildConfiguration="Release" revealArchiveInOrganizer="YES"/>
</Scheme>
""")
    print(f"Wrote project for {len(swift_app)} app sources and {len(swift_tests)} test sources.")


if __name__ == "__main__":
    main()
