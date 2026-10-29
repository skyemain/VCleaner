package com.skyemain.clenzy;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.core.app.ApplicationProvider;
import android.content.Context;
import android.os.CancellationSignal;
import org.junit.*;
import org.junit.runner.RunWith;
import java.io.*;
import java.nio.file.*;
import java.util.*;
import static org.junit.Assert.*;
@RunWith(AndroidJUnit4.class)
public class SharedStorageTest {
 private void shell(String command) throws Exception {
  try(var fd=androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().getUiAutomation().executeShellCommand(command);var stream=new android.os.ParcelFileDescriptor.AutoCloseInputStream(fd)){while(stream.read()!=-1){}}
 }
 @Test public void duplicates_tool_scans_without_picker_and_bulk_deletes_one_copy() throws Exception {
  Context context=ApplicationProvider.getApplicationContext();
  File folder=new File(android.os.Environment.getExternalStoragePublicDirectory(android.os.Environment.DIRECTORY_DOWNLOADS),"Clenzy-test-"+UUID.randomUUID());
  File first=new File(folder,"first.bin"),second=new File(folder,"second.bin");
  try{
   shell("appops set --uid "+context.getPackageName()+" MANAGE_EXTERNAL_STORAGE allow");
   assertTrue(android.os.Environment.isExternalStorageManager());assertTrue(folder.mkdirs());
   byte[] bytes=new byte[357];new java.util.Random().nextBytes(bytes);Files.write(first.toPath(),bytes);Files.write(second.toPath(),bytes);
   android.content.Intent intent=new android.content.Intent(context,MainActivity.class).putExtra("tool","duplicates");
   try(var scenario=androidx.test.core.app.ActivityScenario.<MainActivity>launch(intent)){
    var device=androidx.test.uiautomator.UiDevice.getInstance(androidx.test.platform.app.InstrumentationRegistry.getInstrumentation());
    assertTrue(device.wait(androidx.test.uiautomator.Until.hasObject(androidx.test.uiautomator.By.text("first.bin")),15000));
    var select=device.findObject(androidx.test.uiautomator.By.text("Select extra copies"));assertNotNull(select);select.click();
    var clean=device.wait(androidx.test.uiautomator.Until.findObject(androidx.test.uiautomator.By.text(java.util.regex.Pattern.compile("CLEAN SELECTED.*")).enabled(true)),5000);assertNotNull(clean);clean.click();
    var confirm=device.wait(androidx.test.uiautomator.Until.findObject(androidx.test.uiautomator.By.text(java.util.regex.Pattern.compile("(?i)Continue"))),5000);assertNotNull(confirm);confirm.click();
    assertTrue(device.wait(androidx.test.uiautomator.Until.hasObject(androidx.test.uiautomator.By.text("CLEANING COMPLETE")),10000));assertTrue(first.exists()^second.exists());
   }
  }finally{first.delete();second.delete();folder.delete();shell("appops set --uid "+context.getPackageName()+" MANAGE_EXTERNAL_STORAGE default");}
 }
 @Test public void scan_groups_bulk_selects_and_deletes_only_extra_copy() throws Exception {
  File root=new File(ApplicationProvider.<Context>getApplicationContext().getCacheDir(),"shared-"+UUID.randomUUID());assertTrue(root.mkdirs());
  File a=new File(root,"a.txt"),b=new File(root,"b.txt"),other=new File(root,"other.txt");
  try{
   Files.write(a.toPath(),new byte[]{1,2,3});Files.write(b.toPath(),new byte[]{1,2,3});Files.write(other.toPath(),new byte[]{3,2,1});
   SharedStorageCleaner cleaner=new SharedStorageCleaner(root);var scan=cleaner.scan(new CancellationSignal(),true,false);
   assertEquals(3,scan.files().size());assertEquals(2,ReviewRules.filter(scan.files(),ReviewRules.Category.DUPLICATES,0).size());
   Set<String> selected=ReviewRules.extraCopies(scan.files());assertEquals(1,selected.size());
   List<ReviewFile> chosen=new ArrayList<>();for(var f:scan.files())if(selected.contains(f.uri()))chosen.add(f);
   var outcome=cleaner.delete(chosen,new CancellationSignal());assertEquals(1,outcome.deleted());assertEquals(3,outcome.bytes());assertTrue(other.exists());assertTrue(a.exists()||b.exists());
  }finally{a.delete();b.delete();other.delete();root.delete();}
 }
 @Test public void duplicate_cleanup_keeps_last_copy_when_original_disappears() throws Exception {
  File root=new File(ApplicationProvider.<Context>getApplicationContext().getCacheDir(),"shared-"+UUID.randomUUID());assertTrue(root.mkdirs());
  File a=new File(root,"a"),b=new File(root,"b");
  try{Files.write(a.toPath(),new byte[]{1,2});Files.write(b.toPath(),new byte[]{1,2});
   SharedStorageCleaner cleaner=new SharedStorageCleaner(root);var scan=cleaner.scan(new CancellationSignal(),true,false);
   Set<String> extras=ReviewRules.extraCopies(scan.files());List<ReviewFile> chosen=new ArrayList<>();
   for(var f:scan.files())if(extras.contains(f.uri()))chosen.add(f);else assertTrue(new File(java.net.URI.create(f.uri())).delete());
   assertEquals(0,cleaner.delete(chosen,new CancellationSignal()).deleted());assertTrue(a.exists()||b.exists());
  }finally{a.delete();b.delete();root.delete();}
 }
 @Test public void downloads_scan_excludes_other_files_and_rejects_changed_files() throws Exception {
  File root=new File(ApplicationProvider.<Context>getApplicationContext().getCacheDir(),"shared-"+UUID.randomUUID());File downloads=new File(root,"Download");assertTrue(downloads.mkdirs());
  File file=new File(downloads,"document.txt"),outside=new File(root,"outside.txt");
  try{
   Files.write(file.toPath(),new byte[]{1,2,3});Files.write(outside.toPath(),new byte[]{1});
   SharedStorageCleaner cleaner=new SharedStorageCleaner(root);var scan=cleaner.scan(new CancellationSignal(),false,true);assertEquals(1,scan.files().size());
   Files.write(file.toPath(),new byte[]{4,5,6,7});assertEquals(0,cleaner.delete(scan.files(),new CancellationSignal()).deleted());assertTrue(file.exists());
  }finally{file.delete();outside.delete();downloads.delete();root.delete();}
 }
 @Test public void scan_does_not_follow_links_or_enter_private_android_data() throws Exception {
  File root=new File(ApplicationProvider.<Context>getApplicationContext().getCacheDir(),"shared-"+UUID.randomUUID());File hidden=new File(root,"Android/data");assertTrue(hidden.mkdirs());
  File secret=new File(hidden,"secret.txt"),link=new File(root,"link.txt");
  try{Files.write(secret.toPath(),new byte[]{1});Files.createSymbolicLink(link.toPath(),secret.toPath());
   assertEquals(0,new SharedStorageCleaner(root).scan(new CancellationSignal(),true,false).files().size());
  }finally{link.delete();secret.delete();hidden.delete();hidden.getParentFile().delete();root.delete();}
 }
}
