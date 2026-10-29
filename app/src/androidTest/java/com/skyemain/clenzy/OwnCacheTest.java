package com.skyemain.clenzy;
import android.content.Context;
import android.os.CancellationSignal;
import androidx.test.core.app.ApplicationProvider;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.*;
import org.junit.runner.RunWith;
import java.io.*;
import java.util.*;
import static org.junit.Assert.*;
@RunWith(AndroidJUnit4.class)
public class OwnCacheTest {
 @Test public void cache_deletes_only_observed_old_unchanged_files() throws Exception {
  Context context=ApplicationProvider.getApplicationContext();File root=new File(context.getCacheDir(),"fixture-"+UUID.randomUUID());assertTrue(root.mkdirs());
  File old=new File(root,"old.cache"),fresh=new File(root,"fresh.cache");
  try {
   try(FileOutputStream out=new FileOutputStream(old)){out.write(new byte[123]);}old.setLastModified(System.currentTimeMillis()-172800000L);
   try(FileOutputStream out=new FileOutputStream(fresh)){out.write(1);}
   OwnCacheCleaner cleaner=new OwnCacheCleaner(root);List<ReviewFile> files=cleaner.scan(new CancellationSignal());assertEquals(1,files.size());
   var result=cleaner.delete(files,new CancellationSignal());assertEquals(1,result.deleted());assertEquals(123,result.bytes());assertTrue(fresh.exists());
  }finally{old.delete();fresh.delete();root.delete();}
 }
 @Test public void cache_refuses_files_changed_since_scan() throws Exception {
  Context context=ApplicationProvider.getApplicationContext();File root=new File(context.getCacheDir(),"fixture-"+UUID.randomUUID());assertTrue(root.mkdirs());File old=new File(root,"old.cache");
  try {try(FileOutputStream out=new FileOutputStream(old)){out.write(1);}old.setLastModified(System.currentTimeMillis()-172800000L);
   OwnCacheCleaner cleaner=new OwnCacheCleaner(root);var files=cleaner.scan(new CancellationSignal());try(FileOutputStream out=new FileOutputStream(old)){out.write(new byte[10]);}
   assertEquals(0,cleaner.delete(files,new CancellationSignal()).deleted());assertTrue(old.exists());
  }finally{old.delete();root.delete();}
 }
}
