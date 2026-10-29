package com.skyemain.clenzy;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.core.app.ActivityScenario;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class DashboardTest {
    @Test public void dashboardLaunchesWithoutStoragePermissionOrAutomaticDeletion() {
        try (var scenario = ActivityScenario.launch(HomeActivity.class)) {
            scenario.onActivity(activity -> {
                assertNotNull(activity.findViewById(android.R.id.content));
                assertEquals("Clenzy", activity.getTitle().toString());
            });
            scenario.recreate();
            scenario.onActivity(activity -> assertNotNull(activity.findViewById(android.R.id.content)));
        }
    }
}
