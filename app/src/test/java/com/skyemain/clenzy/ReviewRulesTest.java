package com.skyemain.clenzy;
import org.junit.Test;
import java.util.List;
import static org.junit.Assert.*;

public class ReviewRulesTest {
    @Test public void exactDuplicatesRequireReadableFullHashesAndKeepBothForReview() {
        var a = new ReviewFile("a", "a", 10, 100, "same");
        var b = new ReviewFile("b", "b", 10, 100, "same");
        var unknown = new ReviewFile("c", "c", -1, 0, null);
        assertEquals(2, ReviewRules.filter(List.of(a,b,unknown), ReviewRules.Category.DUPLICATES, 0).size());
        assertTrue(ReviewRules.filter(List.of(a,unknown), ReviewRules.Category.DUPLICATES, 0).isEmpty());
    }
    @Test public void changedAndUnknownMetadataCannotBeDeleted() {
        var a = new ReviewFile("a", "a", 10, 100, null);
        assertTrue(a.matches(10,100)); assertFalse(a.matches(11,100)); assertFalse(a.matches(10,101));
        assertFalse(new ReviewFile("b","b",-1,0,null).matches(-1,0));
    }
    @Test public void oldAndLargeFiltersDoNotInventUnknownValues() {
        var large = new ReviewFile("a","a",104857600,1,null);
        var unknown = new ReviewFile("b","b",-1,0,null);
        assertEquals(List.of(large), ReviewRules.filter(List.of(large,unknown), ReviewRules.Category.LARGE,0));
        assertEquals(List.of(large), ReviewRules.filter(List.of(large,unknown), ReviewRules.Category.OLD, 40L*24*60*60*1000));
    }
    @org.junit.Test public void duplicateGroupsWarnWhenEveryCopyIsSelected() {
        var a = new ReviewFile("a", "first", 10, 100, "same");
        var b = new ReviewFile("b", "second", 10, 200, "same");
        org.junit.Assert.assertEquals("a", ReviewRules.group(java.util.List.of(b,a),"same").get(0).uri());
        org.junit.Assert.assertTrue(ReviewRules.selectsEveryCopy(java.util.List.of(a,b),java.util.Set.of("a","b")));
        org.junit.Assert.assertFalse(ReviewRules.selectsEveryCopy(java.util.List.of(a,b),java.util.Set.of("b")));
    }
}
