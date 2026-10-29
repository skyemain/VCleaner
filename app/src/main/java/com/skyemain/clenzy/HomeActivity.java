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
    private boolean waitingUsage;
    private final java.util.concurrent.ExecutorService worker=java.util.concurrent.Executors.newSingleThreadExecutor();
    @Override protected void onDestroy(){worker.shutdownNow();super.onDestroy();}
    private int dp(float n){return Math.round(n*getResources().getDisplayMetrics().density);}
    @Override public void onCreate(Bundle state){
        int theme=getSharedPreferences("cleanup",0).getInt("theme",-1);
        applyTheme(theme); super.onCreate(state); draw();
    }
    @Override protected void onResume(){super.onResume(); if(page!=null) draw();if(waitingUsage){waitingUsage=false;loadApps();}}
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
        scroll.setOnApplyWindowInsetsListener((v,insets)->{page.setPadding(dp(24),insets.getSystemWindowInsetTop()+dp(16),dp(24),insets.getSystemWindowInsetBottom()+dp(20));return insets;});
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
        LinearLayout row2=new LinearLayout(this);tool(row2,"↓  Downloads","Review downloaded files",()->open("downloads"));tool(row2,"▤  Apps","Find space-heavy apps",this::apps);page.addView(row2);
        long last=getSharedPreferences("cleanup",0).getLong("lastClean",0);
        if(last>0)page.addView(text("Last cleanup · "+android.text.format.DateFormat.format("MMM d, h:mm a",last)+"\n"+size(getSharedPreferences("cleanup",0).getLong("lastBytes",0))+" successfully deleted",13,muted),spaced());
        else page.addView(text("One storage permission. Find large files, downloads and duplicate copies. Review your selection, then clean.",13,muted),spaced());
        setContentView(scroll);
    }
    private void startCleaning(){open("storage");}
    private void open(String tool){startActivity(new Intent(this,MainActivity.class).putExtra("tool",tool));}
    private void settings(){
        new AlertDialog.Builder(this).setTitle("Settings").setItems(new String[]{"Theme","Cleanup history","Privacy & access","System storage cleanup","About Clenzy","Photos & videos","Clenzy cache"},(d,w)->{
            if(w==0)new AlertDialog.Builder(this).setTitle("Theme").setSingleChoiceItems(new String[]{"System","Light","Dark"},getSharedPreferences("cleanup",0).getInt("theme",-1)==-1?0:getSharedPreferences("cleanup",0).getInt("theme",-1)==1?1:2,(dialog,index)->{int mode=index==0?-1:index==1?1:2;getSharedPreferences("cleanup",0).edit().putInt("theme",mode).apply();dialog.dismiss();applyTheme(mode);}).show();
            if(w==1)history();
            if(w==2)message("Private by design","Files stay on your device. Folder access is limited to locations you choose. Media access can be partial. No advertising, tracking, root or Accessibility service. Shared storage scanning uses optional all-files access. Trash may retain storage until emptied in the gallery.");
            if(w==3){try{startActivity(new Intent(android.os.storage.StorageManager.ACTION_MANAGE_STORAGE));}catch(ActivityNotFoundException e){startActivity(new Intent(Settings.ACTION_INTERNAL_STORAGE_SETTINGS));}}
            if(w==5)open("photos");
            if(w==6)open("cache");
            if(w==4)message("Clenzy 1.3.0","Offline storage review and cleanup. Other apps’ private cache cannot be cleared by Clenzy. No speed or battery claims.");
        }).show();
    }
    private void history(){
        try {
            org.json.JSONArray entries=new org.json.JSONArray(getSharedPreferences("cleanup",0).getString("history","[]"));
            if(entries.length()==0){message("Cleanup history","No completed cleanup yet.");return;}
            String[] rows=new String[entries.length()];
            for(int n=0;n<entries.length();n++){var entry=entries.getJSONObject(n);rows[n]=android.text.format.DateFormat.format("MMM d, h:mm a",entry.getLong("time"))+" · "+size(entry.getLong("bytes"))+" · "+entry.getInt("files")+" files";}
            new AlertDialog.Builder(this).setTitle("Cleanup history · on this device").setItems(rows,(d,w)->{try{var item=entries.getJSONObject(w);message("Cleanup details",rows[w]+"\n"+item.getInt("skipped")+" skipped or unavailable. Source bytes deleted may differ from device free-space changes.");}catch(org.json.JSONException e){message("Unavailable","This history entry could not be read.");}}).setPositiveButton("Done",null).show();
        }catch(org.json.JSONException e){message("History unavailable","Saved cleanup history could not be read.");}
    }
    private boolean usageAllowed(){
        android.app.AppOpsManager ops=getSystemService(android.app.AppOpsManager.class);
        return ops.checkOpNoThrow(android.app.AppOpsManager.OPSTR_GET_USAGE_STATS,android.os.Process.myUid(),getPackageName())==android.app.AppOpsManager.MODE_ALLOWED;
    }
    private void apps(){
        if(usageAllowed()){loadApps();return;}
        new AlertDialog.Builder(this).setTitle("Find apps taking up space")
            .setMessage("Optional Usage access lets Clenzy show app sizes and recent use, so you can decide what to uninstall. This information stays on your phone.")
            .setPositiveButton("Enable usage access",(d,w)->{waitingUsage=true;startActivity(new Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS));})
            .setNeutralButton("Continue without sizes",(d,w)->loadApps()).setNegativeButton("Cancel",null).show();
    }
    private record AppEntry(String name,String pkg,long bytes,long cache,long lastUsed) {}
    private void loadApps(){
        android.app.AlertDialog loading=new AlertDialog.Builder(this).setTitle("Analyzing apps…").setMessage("Reading storage reported by Android").setNegativeButton("Cancel",null).create();loading.show();
        worker.execute(()->{
            boolean allowed=usageAllowed();List<AppEntry> entries=new ArrayList<>();Set<String> seen=new HashSet<>();
            Map<String,android.app.usage.UsageStats> usage=Map.of();
            if(allowed)try{var stats=getSystemService(android.app.usage.UsageStatsManager.class).queryAndAggregateUsageStats(System.currentTimeMillis()-90L*86400000,System.currentTimeMillis());if(stats!=null)usage=stats;}catch(SecurityException ignored){}
            Intent launcher=new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
            for(ResolveInfo info:getPackageManager().queryIntentActivities(launcher,0)){
                String pkg=info.activityInfo.packageName;if(!seen.add(pkg))continue;
                long bytes=-1,cache=-1,last=0;
                if(allowed)try{var app=getPackageManager().getApplicationInfo(pkg,0);var stats=getSystemService(android.app.usage.StorageStatsManager.class).queryStatsForPackage(app.storageUuid==null?android.os.storage.StorageManager.UUID_DEFAULT:app.storageUuid,pkg,android.os.Process.myUserHandle());bytes=stats.getAppBytes()+stats.getDataBytes();cache=stats.getCacheBytes();}catch(Exception ignored){}
                if(usage.containsKey(pkg))last=usage.get(pkg).getLastTimeUsed();
                entries.add(new AppEntry(info.loadLabel(getPackageManager()).toString(),pkg,bytes,cache,last));
            }
            entries.sort(Comparator.comparingLong(AppEntry::bytes).reversed().thenComparing(AppEntry::name));
            runOnUiThread(()->{if(isFinishing()||isDestroyed()||!loading.isShowing())return;loading.dismiss();
                String[] labels=new String[entries.size()];for(int n=0;n<entries.size();n++){AppEntry app=entries.get(n);labels[n]=app.name()+" · "+(app.bytes()<0?"Size unavailable":size(app.bytes()))+"\n"+(app.lastUsed()>0?"Last used "+android.text.format.DateFormat.format("MMM d",app.lastUsed()):"No usage record available");}
                new AlertDialog.Builder(this).setTitle("Apps · largest first").setItems(labels,(d,w)->{
                    AppEntry app=entries.get(w);new AlertDialog.Builder(this).setTitle(app.name()).setMessage((app.cache()<0?"":size(app.cache())+" cache included in app data.\n")+"Open Android app settings to clear its cache, or uninstall it. Uninstalling can remove its local data.")
                    .setPositiveButton("App settings",(a,b)->startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,Uri.parse("package:"+app.pkg()))))
                    .setNeutralButton("Uninstall",(a,b)->{try{startActivity(new Intent(Intent.ACTION_DELETE,Uri.parse("package:"+app.pkg())));}catch(ActivityNotFoundException e){message("Unavailable","Use Android app settings to uninstall.");}}).setNegativeButton("Cancel",null).show();
                }).setPositiveButton("Done",null).show();
            });
        });
    }
    private void message(String title,String body){new AlertDialog.Builder(this).setTitle(title).setMessage(body).setPositiveButton("Done",null).show();}
    private LinearLayout column(){LinearLayout v=new LinearLayout(this);v.setOrientation(LinearLayout.VERTICAL);return v;}
    private TextView text(String value,int size,int color){TextView v=new TextView(this);v.setText(value);v.setTextSize(size);v.setTextColor(color);v.setPadding(0,dp(7),0,dp(7));return v;}
    private LinearLayout card(){LinearLayout v=column();GradientDrawable shape=new GradientDrawable();shape.setColor(surface);shape.setCornerRadius(dp(24));v.setBackground(shape);v.setPadding(dp(22),dp(18),dp(22),dp(18));return v;}
    private LinearLayout.LayoutParams spaced(){LinearLayout.LayoutParams v=new LinearLayout.LayoutParams(-1,-2);v.setMargins(0,dp(18),0,dp(18));return v;}
    private MaterialButton button(String text,Runnable action,boolean primary){MaterialButton b=new MaterialButton(this);b.setText(text);b.setAllCaps(false);b.setCornerRadius(dp(20));b.setMinHeight(dp(48));b.setTextColor(primary?Color.WHITE:((getResources().getConfiguration().uiMode&48)==32?Color.rgb(140,176,255):blue));b.setBackgroundTintList(android.content.res.ColorStateList.valueOf(primary?blue:surface));b.setOnClickListener(v->action.run());return b;}
    private void tool(LinearLayout row,String title,String subtitle,Runnable action){LinearLayout c=card();TextView heading=text(title,16,ink);heading.setMinLines(2);c.addView(heading);TextView detail=text(subtitle,12,muted);detail.setMinLines(2);c.addView(detail);c.setMinimumHeight(dp(118));c.setFocusable(true);c.setContentDescription(title+". "+subtitle);c.setOnClickListener(v->action.run());LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(0,-2,1);lp.setMargins(dp(4),dp(4),dp(4),dp(4));row.addView(c,lp);}
    private String size(long bytes){return Formatter.formatFileSize(this,bytes);}
}
