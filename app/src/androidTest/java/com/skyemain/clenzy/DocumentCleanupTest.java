package com.skyemain.clenzy;
import android.net.Uri;
import android.os.CancellationSignal;
import android.provider.DocumentsContract;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.io.File;
import java.nio.file.Files;
import java.util.List;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class DocumentCleanupTest {
    @Test public void scansRealProviderDeletesOnlySelectedAndSkipsChangedFiles() throws Exception {
        var context = InstrumentationRegistry.getInstrumentation().getContext();
        var root = new File(context.getCacheDir(), "provider-fixtures"); root.mkdirs();
        var a = new File(root,"a.txt"); var b = new File(root,"b.txt");
        try {
            Files.write(a.toPath(), "same bytes".getBytes(java.nio.charset.StandardCharsets.UTF_8)); Files.write(b.toPath(), "same bytes".getBytes(java.nio.charset.StandardCharsets.UTF_8));
            var cleaner = new DocumentCleaner(context.getContentResolver());
            Uri tree = DocumentsContract.buildTreeDocumentUri("com.skyemain.clenzy.test.documents", "root");
            var scan = cleaner.scan(tree, new CancellationSignal());
            var duplicates = ReviewRules.filter(scan.files(), ReviewRules.Category.DUPLICATES, System.currentTimeMillis());
            assertEquals(2, duplicates.size());
            var selected = duplicates.stream().filter(f -> f.name().equals("a.txt")).findFirst().get();
            var outcome = cleaner.delete(List.of(selected), new CancellationSignal());
            assertEquals(1, outcome.deleted()); assertFalse(a.exists()); assertTrue(b.exists());
            var stale = duplicates.stream().filter(f -> f.name().equals("b.txt")).findFirst().get();
            Files.write(b.toPath(), "changed content".getBytes(java.nio.charset.StandardCharsets.UTF_8));
            assertEquals(1, cleaner.delete(List.of(stale), new CancellationSignal()).skipped());
            assertTrue(b.exists());
            assertEquals(1, cleaner.delete(List.of(new ReviewFile("content://elsewhere/not-scanned","x",1,1,null)), new CancellationSignal()).skipped());
        } finally { a.delete(); b.delete(); root.delete(); }
    }
}
