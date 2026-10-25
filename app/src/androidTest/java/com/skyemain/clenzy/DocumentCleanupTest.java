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
    @Test public void scansRealProviderDeletesOnlySelectedAndSkipsChangedFiles() throws Exception {
        var context = InstrumentationRegistry.getInstrumentation().getContext();
        var resolver = context.getContentResolver();
        var endpoint = Uri.parse("content://com.skyemain.clenzy.test.documents");
        try {
            resolver.call(endpoint, "reset", null, null);
            var cleaner = new DocumentCleaner(context.getContentResolver());
            Uri tree = DocumentsContract.buildTreeDocumentUri("com.skyemain.clenzy.test.documents", "root");
            var scan = cleaner.scan(tree, new CancellationSignal());
            var duplicates = ReviewRules.filter(scan.files(), ReviewRules.Category.DUPLICATES, System.currentTimeMillis());
            assertEquals(2, duplicates.size());
            var selected = duplicates.stream().filter(f -> f.name().equals("a.txt")).findFirst().get();
            var outcome = cleaner.delete(List.of(selected), new CancellationSignal());
            assertEquals(1, outcome.deleted());
            var state = resolver.call(endpoint, "state", null, null);
            assertFalse(state.getBoolean("a")); assertTrue(state.getBoolean("b"));
            var stale = duplicates.stream().filter(f -> f.name().equals("b.txt")).findFirst().get();
            resolver.call(endpoint, "change", null, null);
            assertEquals(1, cleaner.delete(List.of(stale), new CancellationSignal()).skipped());
            assertTrue(resolver.call(endpoint, "state", null, null).getBoolean("b"));
            assertEquals(1, cleaner.delete(List.of(new ReviewFile("content://elsewhere/not-scanned","x",1,1,null)), new CancellationSignal()).skipped());
        } finally { resolver.call(endpoint, "clear", null, null); }
    }
}
