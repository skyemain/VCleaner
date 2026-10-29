package com.skyemain.clenzy;
import android.graphics.*;
import android.view.*;
import android.widget.TextView;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.uiautomator.*;
import org.junit.*;
import org.junit.runner.RunWith;
import java.io.*;
import static org.junit.Assert.*;
@RunWith(AndroidJUnit4.class)
public class HomeFlowTest {
 private TextView find(View view,String text){if(view instanceof TextView && text.equals(((TextView)view).getText().toString()))return (TextView)view;if(view instanceof ViewGroup){ViewGroup group=(ViewGroup)view;for(int n=0;n<group.getChildCount();n++){TextView result=find(group.getChildAt(n),text);if(result!=null)return result;}}return null;}
 @Test public void home_primary_action_opens_contextual_cleanup_choice(){
  try(var scenario=ActivityScenario.launch(HomeActivity.class)){
   scenario.onActivity(activity->{TextView start=find(activity.getWindow().getDecorView(),"START CLEANING");assertNotNull(start);start.performClick();});
   UiDevice device=UiDevice.getInstance(InstrumentationRegistry.getInstrumentation());
   assertTrue(device.wait(Until.hasObject(By.text("Scan your storage automatically")),5000));device.pressBack();
  }
 }
 @Test public void guided_cache_cleanup_reaches_verified_results() throws Exception {
  var context=androidx.test.core.app.ApplicationProvider.<android.content.Context>getApplicationContext();
  File fixture=new File(context.getCacheDir(),"guided-"+java.util.UUID.randomUUID()+".cache");
  try {
   try(var output=new FileOutputStream(fixture)){output.write(new byte[256]);}
   assertTrue(fixture.setLastModified(System.currentTimeMillis()-172800000L));
   android.content.Intent intent=new android.content.Intent(context,MainActivity.class).putExtra("tool","cache");
   try(var scenario=ActivityScenario.<MainActivity>launch(intent)){
    UiDevice device=UiDevice.getInstance(InstrumentationRegistry.getInstrumentation());
    var clean=device.wait(Until.findObject(By.text(java.util.regex.Pattern.compile("CLEAN SELECTED.*")).enabled(true)),10000);assertNotNull(clean);
    device.waitForIdle();clean.click();
    var confirm=device.wait(Until.findObject(By.text(java.util.regex.Pattern.compile("(?i)Continue"))),5000);assertNotNull(confirm);confirm.click();
    assertTrue(device.wait(Until.hasObject(By.text("CLEANING COMPLETE")),10000));assertFalse(fixture.exists());
    var done=device.findObject(By.text("DONE"));assertNotNull(done);done.click();
   }
  }finally{fixture.delete();}
 }
 @Test public void capture_light_and_dark_home_without_file_permissions() {
  var context=androidx.test.core.app.ApplicationProvider.<android.content.Context>getApplicationContext();
  try {
   for(int mode:new int[]{1,2}){
    context.getSharedPreferences("cleanup",0).edit().putInt("theme",mode).commit();
    try(var scenario=ActivityScenario.launch(HomeActivity.class)){
     InstrumentationRegistry.getInstrumentation().waitForIdleSync();
     scenario.onActivity(activity->{
      View view=activity.getWindow().getDecorView();assertNotNull(find(view,"START CLEANING"));
      Bitmap bitmap=Bitmap.createBitmap(view.getWidth(),view.getHeight(),Bitmap.Config.ARGB_8888);view.draw(new Canvas(bitmap));
      try(var output=new FileOutputStream(new File(activity.getExternalFilesDir(null),mode==1?"Clenzy-Android-light.png":"Clenzy-Android-dark.png"))){bitmap.compress(Bitmap.CompressFormat.PNG,100,output);}catch(IOException e){throw new AssertionError(e);}finally{bitmap.recycle();}
     });
    }
   }
  }finally{context.getSharedPreferences("cleanup",0).edit().putInt("theme",-1).commit();}
 }
}
