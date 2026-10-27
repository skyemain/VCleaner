package com.skyemain.clenzy;

import android.content.*;
import android.graphics.*;
import android.net.Uri;
import android.os.*;
import android.provider.MediaStore;
import androidx.test.core.app.ActivityScenario;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.lifecycle.ViewModelProvider;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.util.*;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class MediaReviewTest {
    @Test public void mediaDuplicatesRemainReviewableAndStateSurvivesRotation() throws Exception {
        var context = InstrumentationRegistry.getInstrumentation().getTargetContext();
        var resolver = context.getContentResolver();
        List<Uri> created = new ArrayList<>();
        try {
            for (String name : List.of("Screenshot-Clenzy-A.png", "Screenshot-Clenzy-B.png")) {
                ContentValues values = new ContentValues(); values.put(MediaStore.Images.Media.DISPLAY_NAME, name); values.put(MediaStore.Images.Media.MIME_TYPE, "image/png"); values.put(MediaStore.Images.Media.IS_PENDING, 1);
                Uri uri = resolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, values); assertNotNull(uri); created.add(uri);
                try (var output = resolver.openOutputStream(uri)) {
                    Bitmap bitmap = Bitmap.createBitmap(400,400,Bitmap.Config.ARGB_8888);
                    Canvas canvas = new Canvas(bitmap); canvas.drawColor(Color.rgb(18,54,46)); Paint paint = new Paint(); paint.setColor(Color.rgb(68,239,196)); canvas.drawCircle(200,200,110,paint);
                    bitmap.compress(Bitmap.CompressFormat.PNG,100,output); bitmap.recycle();
                }
                ContentValues ready = new ContentValues(); ready.put(MediaStore.Images.Media.IS_PENDING, 0); resolver.update(uri, ready, null, null);
                boolean settled = false;
                for (int attempt = 0; attempt < 50; attempt++) {
                    try (var cursor = resolver.query(uri, new String[]{"_size", "date_modified"}, null, null, null)) {
                        settled = cursor != null && cursor.moveToFirst() && cursor.getLong(0) > 0 && cursor.getLong(1) > 0;
                    }
                    if (settled) break;
                    Thread.sleep(100);
                }
                assertTrue("Fixture metadata did not settle", settled);
            }
            MediaCleaner cleaner = new MediaCleaner(resolver);
            var result = cleaner.scan(new CancellationSignal(), false);
            var matches = ReviewRules.filter(result.files(), ReviewRules.Category.DUPLICATES, System.currentTimeMillis());
            assertTrue("Scan: " + result + "; created: " + created, matches.stream().anyMatch(f -> f.uri().equals(created.get(0).toString())));
            assertTrue("Scan: " + result + "; created: " + created, matches.stream().anyMatch(f -> f.uri().equals(created.get(1).toString())));
            var original = matches.stream().filter(f -> f.uri().equals(created.get(0).toString())).findFirst().get();
            assertEquals(1, cleaner.eligible(List.of(original), new CancellationSignal(), false).size());
            assertTrue(cleaner.eligible(List.of(new ReviewFile("content://unscanned/x", "x", 1,1,null)), new CancellationSignal(), false).isEmpty());
            try (var scenario = ActivityScenario.launch(MainActivity.class)) {
                scenario.onActivity(activity -> {
                    var model = new ViewModelProvider(activity).get(CleanupViewModel.class);
                    model.media = true; model.files = result.files(); model.category = ReviewRules.Category.DUPLICATES; model.selected.add(original.uri()); model.status = "Test library · two real matching images"; model.changed();
                });
                scenario.recreate();
                scenario.onActivity(activity -> {
                    var model = new ViewModelProvider(activity).get(CleanupViewModel.class);
                    assertTrue(model.selected.contains(original.uri())); assertEquals(ReviewRules.Category.DUPLICATES,model.category); assertFalse(model.files.isEmpty());
                });
                androidx.test.uiautomator.UiDevice device = androidx.test.uiautomator.UiDevice.getInstance(InstrumentationRegistry.getInstrumentation());
                device.waitForIdle(); Thread.sleep(800);
                assertTrue(device.takeScreenshot(new java.io.File(context.getExternalFilesDir(null), "Clenzy-Android-review.png")));
            }
            try (var output = resolver.openOutputStream(created.get(0), "wt")) { output.write(new byte[]{1,2,3,4}); }
            assertTrue(cleaner.eligible(List.of(original), new CancellationSignal(), false).isEmpty());
        } finally { for (Uri uri : created) resolver.delete(uri,null,null); }
    }
    @Test public void cancelledMediaScanDoesNotProduceResults() throws Exception {
        var context = InstrumentationRegistry.getInstrumentation().getTargetContext();
        CancellationSignal cancel = new CancellationSignal(); cancel.cancel();
        try { new MediaCleaner(context.getContentResolver()).scan(cancel,false); fail("Expected cancellation"); }
        catch (OperationCanceledException expected) { }
    }
}
