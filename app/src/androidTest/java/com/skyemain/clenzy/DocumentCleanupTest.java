package com.skyemain.clenzy;
import android.net.Uri;
import android.os.CancellationSignal;
import android.provider.DocumentsContract;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.util.List;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class DocumentCleanupTest {
    private boolean clickWhenReady(androidx.test.uiautomator.UiDevice device, androidx.test.uiautomator.BySelector selector, long timeout) throws Exception {
        if (!device.wait(androidx.test.uiautomator.Until.hasObject(selector), timeout)) return false;
        for (int attempt=0; attempt<20; attempt++) {
            try {
                device.waitForIdle();
                var object = device.findObject(selector);
                if (object != null) { object.click(); return true; }
            } catch (androidx.test.uiautomator.StaleObjectException ignored) { }
            Thread.sleep(150);
        }
        return false;
    }

    private String fixtureCall(android.content.ContentResolver resolver, Uri endpoint, String method) throws Exception {
        var automation = InstrumentationRegistry.getInstrumentation().getUiAutomation();
        var descriptor = automation.executeShellCommand("content call --uri content://com.skyemain.clenzy.test.documents --method " + method);
        try (var stream = new android.os.ParcelFileDescriptor.AutoCloseInputStream(descriptor);
             var output = new java.io.ByteArrayOutputStream()) {
            byte[] buffer = new byte[1024]; int count;
            while ((count = stream.read(buffer)) != -1) output.write(buffer, 0, count);
            String result = output.toString("UTF-8");
            assertFalse(result, result.contains("Error") || result.contains("Exception"));
            return result;
        }
    }
    @Test public void scansRealProviderDeletesOnlySelectedAndSkipsChangedFiles() throws Exception {
        var context = InstrumentationRegistry.getInstrumentation().getTargetContext();
        var resolver = context.getContentResolver();
        var endpoint = Uri.parse("content://com.skyemain.clenzy.test.documents");
        try (var scenario = androidx.test.core.app.ActivityScenario.launch(MainActivity.class)) {
            String resetState = fixtureCall(resolver, endpoint, "reset");
            assertTrue("Fixture creation failed: " + resetState, resetState.contains("a=true") && resetState.contains("b=true"));
            var device = androidx.test.uiautomator.UiDevice.getInstance(InstrumentationRegistry.getInstrumentation());
            scenario.onActivity(activity -> {
                var picker = new android.content.Intent(android.content.Intent.ACTION_OPEN_DOCUMENT_TREE);
                picker.putExtra(DocumentsContract.EXTRA_INITIAL_URI, DocumentsContract.buildDocumentUri("com.skyemain.clenzy.test.documents", "root"));
                picker.addFlags(android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION | android.content.Intent.FLAG_GRANT_WRITE_URI_PERMISSION | android.content.Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
                activity.startActivityForResult(picker, 10);
            });
            var rootsSelector = androidx.test.uiautomator.By.desc(java.util.regex.Pattern.compile("Show roots|Open navigation drawer", java.util.regex.Pattern.CASE_INSENSITIVE));
            if (clickWhenReady(device, rootsSelector, 3000)) {
                assertTrue("Fixture provider is missing from Android's folder picker", clickWhenReady(device, androidx.test.uiautomator.By.text("Clenzy test files"), 5000));
            }
            if (!clickWhenReady(device, androidx.test.uiautomator.By.text(java.util.regex.Pattern.compile("Use this folder", java.util.regex.Pattern.CASE_INSENSITIVE)), 10000)) {
                var dump = new java.io.ByteArrayOutputStream(); device.dumpWindowHierarchy(dump);
                fail("Folder picker selection button missing. UI: " + dump.toString("UTF-8"));
            }
            clickWhenReady(device, androidx.test.uiautomator.By.text(java.util.regex.Pattern.compile("Allow", java.util.regex.Pattern.CASE_INSENSITIVE)), 5000);
            device.waitForIdle();
            var chosenTree = new java.util.concurrent.atomic.AtomicReference<String>();
            for (int attempt=0; attempt<30 && chosenTree.get()==null; attempt++) {
                scenario.onActivity(activity -> {
                    try {
                        var field = MainActivity.class.getDeclaredField("tree"); field.setAccessible(true);
                        var uri = (Uri) field.get(activity);
                        if (uri != null) chosenTree.set(uri.toString());
                    } catch (ReflectiveOperationException e) { throw new AssertionError(e); }
                });
                if (chosenTree.get()==null) Thread.sleep(200);
            }
            var hierarchy = new java.io.ByteArrayOutputStream();
            device.dumpWindowHierarchy(hierarchy);
            assertNotNull("No folder result returned to the app; UI: " + hierarchy.toString("UTF-8"), chosenTree.get());
            assertEquals("Wrong picker folder: " + chosenTree.get(), "com.skyemain.clenzy.test.documents", Uri.parse(chosenTree.get()).getAuthority());
            var cleaner = new DocumentCleaner(context.getContentResolver());
            Uri tree = Uri.parse(chosenTree.get());
            var scan = cleaner.scan(tree, new CancellationSignal());
            assertEquals("Tree: " + tree + "; provider: " + fixtureCall(resolver, endpoint, "state") + "; scan: " + scan, 2, scan.files().size());
            for (var file : scan.files()) {
                try (var input = resolver.openInputStream(Uri.parse(file.uri()))) {
                    assertNotNull("Cannot open fixture " + file, input);
                    assertTrue("Empty fixture " + file, input.read() >= 0);
                }
            }
            var duplicates = ReviewRules.filter(scan.files(), ReviewRules.Category.DUPLICATES, System.currentTimeMillis());
            assertEquals("Scan result: " + scan, 2, duplicates.size());
            var selected = duplicates.stream().filter(f -> f.name().equals("a.txt")).findFirst().get();
            var outcome = cleaner.delete(List.of(selected), new CancellationSignal());
            assertEquals(1, outcome.deleted());
            var state = fixtureCall(resolver, endpoint, "state");
            assertTrue(state, state.contains("a=false")); assertTrue(state, state.contains("b=true"));
            var stale = duplicates.stream().filter(f -> f.name().equals("b.txt")).findFirst().get();
            fixtureCall(resolver, endpoint, "change");
            assertEquals(1, cleaner.delete(List.of(stale), new CancellationSignal()).skipped());
            assertTrue(fixtureCall(resolver, endpoint, "state").contains("b=true"));
            assertEquals(1, cleaner.delete(List.of(new ReviewFile("content://elsewhere/not-scanned","x",1,1,null)), new CancellationSignal()).skipped());
        } finally { fixtureCall(resolver, endpoint, "clear"); }
    }
}
