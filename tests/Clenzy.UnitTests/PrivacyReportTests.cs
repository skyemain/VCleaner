using Clenzy.Core.Models;
using Clenzy.Core.Services;
using Xunit;
namespace Clenzy.UnitTests;
public sealed class PrivacyReportTests
{
    [Fact] public void Recent_shortcuts_and_registry_history_share_one_category_without_losing_totals()
    {
        const string id="privacy.activity", category="Recent items";
        var scan = new ScanReport { Modules = new[]{new ModuleScanResult {
            ModuleId=id,ModuleName="Privacy",Group=CleanerGroup.Privacy,
            Findings=new(){new Finding {ModuleId=id,Group=CleanerGroup.Privacy,Category=category,Location="fixture",Description="Recent shortcut",Action=ActionType.DeleteFile}}
        }}};
        var clean = new CleanReport {Modules=new[]{new ModuleCleanResult {
            ModuleId=id,ModuleName="Privacy",Group=CleanerGroup.Privacy,ItemsCleaned=5,ItemsSkipped=1,Errors=1,BytesRecovered=128,
            Categories=new[]{new CategoryResult(id,CleanerGroup.Privacy,category,2,1,0,256,128),new CategoryResult(id,CleanerGroup.Privacy,category,3,0,1,0,0)}
        }}};
        var record=SessionRecorder.Create(scan,clean,PlanMode.Reviewed);
        var result=Assert.Single(record.Categories);
        Assert.Equal(5,result.ItemsCleaned); Assert.Equal(1,result.ItemsSkipped); Assert.Equal(1,result.Errors); Assert.Equal(128,result.RecoveredBytes);
        Assert.Equal(5,record.ItemsCleaned); Assert.Equal(128,record.TotalRecoveredBytes);
    }
}
