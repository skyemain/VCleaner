package com.skyemain.clenzy;
import org.junit.Test;
import java.util.*;
import static org.junit.Assert.*;
public class SelectionTotalsTest {
 @Test public void totals_only_selected_known_sizes_and_counts_uri_once(){
  ReviewFile a=new ReviewFile("content://a","a",25,1,null),b=new ReviewFile("content://b","b",50,1,null),unknown=new ReviewFile("content://c","c",-1,1,null);
  assertEquals(25,ReviewRules.selectedBytes(List.of(a,a,b,unknown),Set.of(a.uri(),unknown.uri())));
  assertEquals(0,ReviewRules.selectedBytes(List.of(a,b),Set.of()));
 }
 @Test public void all_files_review_does_not_drop_small_downloads(){
  ReviewFile f=new ReviewFile("content://a","document.pdf",5,1,null);
  assertEquals(1,ReviewRules.filter(List.of(f),ReviewRules.Category.ALL,System.currentTimeMillis()).size());
 }
}
