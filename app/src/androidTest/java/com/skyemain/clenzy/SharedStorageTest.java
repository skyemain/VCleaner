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
