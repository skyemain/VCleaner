package com.skyemain.clenzy;

import android.app.AlertDialog;
import android.content.*;
import android.content.pm.*;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.*;
import android.provider.Settings;
import android.text.format.Formatter;
import android.view.*;
import android.widget.*;
import androidx.appcompat.app.AppCompatActivity;
import androidx.appcompat.app.AppCompatDelegate;
import com.google.android.material.button.MaterialButton;
import java.util.*;

public final class HomeActivity extends AppCompatActivity {
    private int ink,muted,surface,bg; private final int blue=Color.rgb(50,104,232);
    private LinearLayout page;
    private final java.util.concurrent.ExecutorService worker=java.util.concurrent.Executors.newSingleThreadExecutor();
    @Override protected void onDestroy(){worker.shutdownNow();super.onDestroy();}
    private int dp(float n){return Math.round(n*getResources().getDisplayMetrics().density);}
    @Override public void onCreate(Bundle state){
        int theme=getSharedPreferences("cleanup",0).getInt("theme",-1);
        applyTheme(theme); super.onCreate(state); draw();
    }
    @Override protected void onResume(){super.onResume(); if(page!=null) draw();}
    private static void applyTheme(int mode){
        if(mode==1)AppCompatDelegate.setDefaultNightMode(AppCompatDelegate.MODE_NIGHT_NO);
        else if(mode==2)AppCompatDelegate.setDefaultNightMode(AppCompatDelegate.MODE_NIGHT_YES);
        else AppCompatDelegate.setDefaultNightMode(AppCompatDelegate.MODE_NIGHT_FOLLOW_SYSTEM);
    }
    private void draw(){
        boolean dark=(getResources().getConfiguration().uiMode&48)==32;
        ink=Color.parseColor(dark?"#F3F6FF":"#172137");muted=Color.parseColor(dark?"#A7B3C9":"#647087");surface=Color.parseColor(dark?"#192437":"#FFFFFF");bg=Color.parseColor(dark?"#0E1726":"#F3F6FC");
        getWindow().setStatusBarColor(bg);getWindow().setNavigationBarColor(bg);
        getWindow().getDecorView().setSystemUiVisibility(dark?0:View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR|View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);
        ScrollView scroll=new ScrollView(this);scroll.setFillViewport(true);scroll.setBackgroundColor(bg);
        page=column();page.setPadding(dp(24),dp(22),dp(24),dp(24));scroll.addView(page);
        LinearLayout header=new LinearLayout(this);header.setGravity(Gravity.CENTER_VERTICAL);
        TextView brand=text("✦  Clenzy",26,ink);brand.setTypeface(null,Typeface.BOLD);header.addView(brand,new LinearLayout.LayoutParams(0,-2,1));
        MaterialButton settings=button("Settings",this::settings,false);header.addView(settings);page.addView(header);
        TextView heading=text("Make room for what matters.",19,muted);page.addView(heading);
        StatFs disk=new StatFs(Environment.getDataDirectory().getAbsolutePath());long total=disk.getTotalBytes(),free=disk.getAvailableBytes(),used=total-free;
        LinearLayout card=card();card.addView(text("AVAILABLE STORAGE",12,muted));TextView amount=text(size(free),42,ink);amount.setTypeface(null,Typeface.BOLD);card.addView(amount);
        card.addView(text(size(used)+" used of "+size(total),15,muted));
        ProgressBar bar=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);bar.setMax(1000);bar.setProgress(total>0?(int)(1000.0*used/total):0);bar.setProgressTintList(android.content.res.ColorStateList.valueOf(blue));card.addView(bar,new LinearLayout.LayoutParams(-1,dp(12)));
        card.addView(text((total>0?(int)(100.0*used/total):0)+"% used · measured on this device",13,muted));page.addView(card,spaced());
        MaterialButton start=button("START CLEANING",this::startCleaning,true);start.setMinHeight(dp(60));page.addView(start,new LinearLayout.LayoutParams(-1,-2));
        page.addView(text("Review your files before removing anything personal.",13,muted));
        page.addView(text("YOUR TOOLS",12,muted),spaced());
        LinearLayout row=new LinearLayout(this);tool(row,"▣  Large Files","Find files over 100 MB",()->open("large"));tool(row,"▧  Duplicate Files","Compare exact contents",()->open("duplicates"));page.addView(row);
        LinearLayout row2=new LinearLayout(this);tool(row2,"↓  Downloads","Choose a folder to review",()->open("downloads"));tool(row2,"▤  Apps","Manage installed apps",this::apps);page.addView(row2);
        long last=getSharedPreferences("cleanup",0).getLong("lastClean",0);
        if(last>0)page.addView(text("Last cleanup · "+android.text.format.DateFormat.format("MMM d, h:mm a",last)+"\n"+size(getSharedPreferences("cleanup",0).getLong("lastBytes",0))+" successfully deleted",13,muted),spaced());
        else page.addView(text("Clenzy helps you review accessible files and remove its own disposable cache. Other apps’ private caches remain protected by Android.",13,muted),spaced());
        setContentView(scroll);
    }
    private void startCleaning(){
        new AlertDialog.Builder(this).setTitle("Choose what to clean").setMessage("Start with Clenzy’s disposable cache, review photos and videos, or grant access to a folder. Personal files are never selected automatically.")
        .setPositiveButton("Clenzy cache",(d,w)->open("cache"))
        .setNeutralButton("Photos & videos",(d,w)->open("photos"))
        .setNegativeButton("Choose folder",(d,w)->open("folder")).show();
    }
    private void open(String tool){startActivity(new Intent(this,MainActivity.class).putExtra("tool",tool));}
    private void settings(){
        new AlertDialog.Builder(this).setTitle("Settings").setItems(new String[]{"Theme","Cleanup history","Privacy & access","System storage cleanup","About Clenzy"},(d,w)->{
            if(w==0)new AlertDialog.Builder(this).setTitle("Theme").setSingleChoiceItems(new String[]{"System","Light","Dark"},getSharedPreferences("cleanup",0).getInt("theme",-1)==-1?0:getSharedPreferences("cleanup",0).getInt("theme",-1)==1?1:2,(dialog,index)->{int mode=index==0?-1:index==1?1:2;getSharedPreferences("cleanup",0).edit().putInt("theme",mode).apply();dialog.dismiss();applyTheme(mode);}).show();
            if(w==1){long last=getSharedPreferences("cleanup",0).getLong("lastClean",0);message("Last cleanup",last==0?"No completed cleanup yet.":android.text.format.DateFormat.format("MMM d, yyyy h:mm a",last)+"\n"+size(getSharedPreferences("cleanup",0).getLong("lastBytes",0))+" deleted from selected files.");}
            if(w==2)message("Private by design","Files stay on your device. Folder access is limited to locations you choose. Media access can be partial. No advertising, tracking, root, Accessibility or all-files access. Trash may retain storage until emptied in the gallery.");
            if(w==3){try{startActivity(new Intent(android.os.storage.StorageManager.ACTION_MANAGE_STORAGE));}catch(ActivityNotFoundException e){startActivity(new Intent(Settings.ACTION_INTERNAL_STORAGE_SETTINGS));}}
            if(w==4)message("Clenzy 1.2.0","Offline storage review and cleanup. Other apps’ private cache cannot be cleared by Clenzy. No speed or battery claims.");
        }).show();
    }
    private void apps(){
        worker.execute(()->{
            Intent launcher=new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
            Map<String,String> visible=new TreeMap<>(String.CASE_INSENSITIVE_ORDER);
            for(ResolveInfo r:getPackageManager().queryIntentActivities(launcher,0))visible.put(r.loadLabel(getPackageManager()).toString()+" · "+r.activityInfo.packageName,r.activityInfo.packageName);
            runOnUiThread(()->{if(isFinishing())return;String[] labels=visible.keySet().toArray(new String[0]);
                new AlertDialog.Builder(this).setTitle("Apps · visible launcher apps").setItems(labels,(d,w)->{String pkg=visible.get(labels[w]);new AlertDialog.Builder(this).setTitle(labels[w]).setMessage("Android controls app storage and uninstall. Private app storage sizes are not available to Clenzy.").setPositiveButton("App settings",(a,b)->startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,Uri.parse("package:"+pkg)))).setNeutralButton("Uninstall",(a,b)->{try{startActivity(new Intent(Intent.ACTION_DELETE,Uri.parse("package:"+pkg)));}catch(ActivityNotFoundException e){message("Unavailable","Open Android Settings to uninstall this app.");}}).setNegativeButton("Cancel",null).show();}).setPositiveButton("All apps in Settings",(d,w)->startActivity(new Intent(Settings.ACTION_MANAGE_APPLICATIONS_SETTINGS))).show();});
        });
    }
    private void message(String title,String body){new AlertDialog.Builder(this).setTitle(title).setMessage(body).setPositiveButton("Done",null).show();}
    private LinearLayout column(){LinearLayout v=new LinearLayout(this);v.setOrientation(1);return v;}
    private TextView text(String value,int size,int color){TextView v=new TextView(this);v.setText(value);v.setTextSize(size);v.setTextColor(color);v.setPadding(0,dp(7),0,dp(7));return v;}
    private LinearLayout card(){LinearLayout v=column();GradientDrawable shape=new GradientDrawable();shape.setColor(surface);shape.setCornerRadius(dp(24));v.setBackground(shape);v.setPadding(dp(22),dp(18),dp(22),dp(18));return v;}
    private LinearLayout.LayoutParams spaced(){LinearLayout.LayoutParams v=new LinearLayout.LayoutParams(-1,-2);v.setMargins(0,dp(18),0,dp(18));return v;}
    private MaterialButton button(String text,Runnable action,boolean primary){MaterialButton b=new MaterialButton(this);b.setText(text);b.setAllCaps(false);b.setCornerRadius(dp(20));b.setMinHeight(dp(48));b.setTextColor(primary?Color.WHITE:blue);b.setBackgroundTintList(android.content.res.ColorStateList.valueOf(primary?blue:surface));b.setOnClickListener(v->action.run());return b;}
    private void tool(LinearLayout row,String title,String subtitle,Runnable action){LinearLayout c=card();c.addView(text(title,16,ink));c.addView(text(subtitle,12,muted));c.setMinimumHeight(dp(118));c.setFocusable(true);c.setContentDescription(title+". "+subtitle);c.setOnClickListener(v->action.run());LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(0,-2,1);lp.setMargins(dp(4),dp(4),dp(4),dp(4));row.addView(c,lp);}
    private String size(long bytes){return Formatter.formatFileSize(this,bytes);}
}
