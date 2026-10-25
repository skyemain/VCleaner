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
            fixtureCall(resolver, endpoint, "reset");
            var device = androidx.test.uiautomator.UiDevice.getInstance(InstrumentationRegistry.getInstrumentation());
            scenario.onActivity(activity -> {
                var picker = new android.content.Intent(android.content.Intent.ACTION_OPEN_DOCUMENT_TREE);
                picker.putExtra(DocumentsContract.EXTRA_INITIAL_URI, DocumentsContract.buildDocumentUri("com.skyemain.clenzy.test.documents", "root"));
                picker.addFlags(android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION | android.content.Intent.FLAG_GRANT_WRITE_URI_PERMISSION | android.content.Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
                activity.startActivityForResult(picker, 10);
            });
            var useFolder = device.wait(androidx.test.uiautomator.Until.findObject(androidx.test.uiautomator.By.text(java.util.regex.Pattern.compile("Use this folder", java.util.regex.Pattern.CASE_INSENSITIVE))), 10000);
            assertNotNull("Android folder picker did not show the fixture folder", useFolder);
            useFolder.click();
            var allow = device.wait(androidx.test.uiautomator.Until.findObject(androidx.test.uiautomator.By.text(java.util.regex.Pattern.compile("Allow", java.util.regex.Pattern.CASE_INSENSITIVE))), 5000);
            if (allow != null) allow.click();
            device.waitForIdle();
            var grantedTree = DocumentsContract.buildTreeDocumentUri("com.skyemain.clenzy.test.documents", "root");
            for (int attempt=0; attempt<30 && context.checkUriPermission(grantedTree, android.os.Process.myPid(), android.os.Process.myUid(), android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION) != android.content.pm.PackageManager.PERMISSION_GRANTED; attempt++) Thread.sleep(200);
            assertEquals(android.content.pm.PackageManager.PERMISSION_GRANTED, context.checkUriPermission(grantedTree, android.os.Process.myPid(), android.os.Process.myUid(), android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION));
            var cleaner = new DocumentCleaner(context.getContentResolver());
            Uri tree = DocumentsContract.buildTreeDocumentUri("com.skyemain.clenzy.test.documents", "root");
            var scan = cleaner.scan(tree, new CancellationSignal());
            var duplicates = ReviewRules.filter(scan.files(), ReviewRules.Category.DUPLICATES, System.currentTimeMillis());
            assertEquals(2, duplicates.size());
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
