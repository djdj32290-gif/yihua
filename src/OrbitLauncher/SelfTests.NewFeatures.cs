using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitLauncher;
internal static partial class SelfTests
{
    static void VerifySelectionModel(Action<bool,string> check)
    {
        var s=new GestureSelection();
        s.Begin(8,209,58);s.Update(140,0,0);check(s.NeedsDwell,"仅待完成停留时需要计时器");
        s.Update(140,0,150);check(!s.NeedsDwell && !s.Update(140,0,500),"选择稳定后不再产生重绘变化");
        check(ReferenceEquals(s.Stops,s.Stops),"选择列表复用只读视图而不反复分配");
        for(int count=1;count<=16;count++)
        {
            for(int i=0;i<count;i++)
            {
                double a=-Math.PI/2+i*2*Math.PI/count,x=Math.Cos(a),y=Math.Sin(a);
                s.Begin(count,209,58);s.Update(x*140,y*140,0);s.Update(x*140,y*140,150);
                check(s.Stops.Count==1 && s.Stops[0].Index==i && !s.Confirmed,$"{count} 项 / {i} 停留选择但不直接确认");
                s.Update(x*208.5,y*208.5,160);check(!s.Confirmed,$"{count} 项 / {i} 轮盘内沿不能确认");
                s.Update(x*209.5,y*209.5,170);check(s.Confirmed && s.Release(true).SequenceEqual(new[]{i}),$"{count} 项 / {i} 刚拉出外沿即可提交且清空");
            }
        }
        s.Begin(8,209,58);s.Update(-140,0,0);s.Update(-140,0,149);
        check(s.Stops.Count==0,"扫过选项不足 150ms 不加入列表");
        s.Update(-140,0,150);s.Update(0,0,160);s.Update(140,0,170);s.Update(140,0,320);
        check(s.Stops.Select(p=>p.Index).SequenceEqual(new[]{6,2}),"穿过中心保留左侧及右侧选择顺序");
        check(s.Stops[0].X==-140 && s.Stops[1].X==140,"路径节点记录真实停留位置");
        s.Update(250,0,330);check(s.Confirmed,"最后右侧选项外拉后整组选中确认");
        s.Update(200,0,340);check(!s.Confirmed,"离开确认区回到圈内立即撤销确认");
        check(s.Release(true).Length==0 && s.Stops.Count==0,"多选未确认松开一个也不启动且清空");
        s.Begin(8,209,58);s.Update(-140,0,0);s.Update(-140,0,150);s.Update(140,0,160);s.Update(140,0,310);s.Update(250,0,320);
        check(s.Release(true).SequenceEqual(new[]{6,2}),"左到右多选在最后确认一次性提交两个入口");
        check(s.Release(true).Length==0,"同一手势不能重复提交");
        s.Begin(8,209,58);s.Update(140,0,0);s.Update(140,0,150);s.Update(0,0,160);s.Update(140,0,170);s.Update(140,0,320);
        check(s.Stops.Count==1,"重复经过同一选项只加入一次");
        s.Update(-250,0,330);check(!s.Confirmed && s.Release(true).Length==0,"从另一个方向出圈不能确认之前的选项");
        s.Begin(8,209,58);s.Update(250,0,0);s.Update(250,0,300);
        check(s.Confirmed && s.Release(true).SequenceEqual(new[]{2}),"首次快速越过轮盘直接选中对应方向");
        s.Begin(8,209,58);s.Update(140,0,0);s.Update(140,0,150);s.Update(250,0,160);
        check(s.Release(false).Length==0 && s.Stops.Count==0,"已进入确认区仍可被 Esc 或修饰键取消");
        s.Begin(0,209,58);s.Update(250,0,300);check(s.Release(true).Length==0,"空轮盘不能确认");
        s.Begin(8,209,58);s.Update(140,0,0);s.Update(140,0,150);s.Update(250,0,160);s.Update(0,0,170);
        check(s.Release(true).Length==0,"确认后返回中心松开仍全部取消");
        var surface=new RadialSurface{Width=632,Height=632};surface.Configure(DemoSettings());
        surface.Measure(new Size(632,632));surface.Arrange(new Rect(0,0,632,632));surface.UpdateLayout();
        surface.Gesture.Begin(8,surface.OuterRadius,58);surface.Gesture.Update(-140,0,0);surface.Gesture.Update(-140,0,150);surface.Gesture.Update(140,0,160);surface.Gesture.Update(140,0,310);surface.Gesture.Update(259,0,320);surface.InvalidateVisual();surface.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1264,1264,192,192,PixelFormats.Pbgra32);bitmap.Render(surface);var pixels=new byte[1264*1264*4];bitmap.CopyPixels(pixels,1264*4,0);
        byte Alpha(double r){int px=(int)Math.Round((316+r*Math.Cos(.22))*2),py=(int)Math.Round((316+r*Math.Sin(.22))*2);return pixels[(py*1264+px)*4+3];}
        check(Alpha(221)>90 && Alpha(290)<40,"外部确认区沿半径平滑渐隐至透明");
        int offset=((316*2)*1264+(316-85)*2)*4;
        check(pixels[offset+1]>180 && pixels[offset+2]<150,"路径线覆盖从中心到停留点的连线");
        VerifySelectedReturns(check);
    }

    static void VerifyBatchDelete(AppController controller,SettingsWindow window,RadialSurface preview,string folder,Action<bool,string> check)
    {
        var original=controller.Config.Items.ToList();
        var toggle=(Button)window.FindName("DeleteModeButton");var remove=(Button)window.FindName("DeleteSelectedButton");
        void Click(int index)
        {
            double a=-Math.PI/2+index*2*Math.PI/preview.Settings.Items.Count,r=preview.ActualWidth/2-40;
            var p=new Point(preview.ActualWidth/2+Math.Cos(a)*r,preview.ActualHeight/2+Math.Sin(a)*r);
            preview.EditorPointerDown(p);preview.EditorPointerUp(p);
        }
        try
        {
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(preview.DeleteMode && !remove.IsEnabled,"删除模式无勾选时不能删除");
            if(original.Count>1)
            {
                Click(0);Click(original.Count-1);check(preview.DeleteSelection.SetEquals(new[]{0,original.Count-1}),"点击两个扇区同时勾选");
                check(controller.Config.Items.SequenceEqual(original),"勾选期间不提前修改配置");
                Click(0);check(preview.DeleteSelection.SetEquals(new[]{original.Count-1}),"再次点击取消单项勾选");Click(0);
                var source=new Point(preview.ActualWidth/2,24);var target=new Point(preview.ActualWidth-24,preview.ActualHeight/2);
                preview.EditorPointerDown(source);preview.EditorPointerMove(target);preview.EditorPointerUp(target);
                check(controller.Config.Items.SequenceEqual(original),"删除模式拖动不会交换或修改选项");
                SaveVisual(window,Path.Combine(folder,"批量删除-1.3.4.png"));
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!preview.DeleteMode && controller.Config.Items.SequenceEqual(original),"退出删除模式保留所有配置");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Click(0);Click(original.Count-1);
                var center=new Point(preview.ActualWidth/2,preview.ActualHeight/2);preview.EditorPointerDown(center);preview.EditorPointerUp(center);
                check(!Application.Current.Windows.OfType<ItemEditorWindow>().Any(),"删除模式中心不会打开新增弹窗");
                var diskBefore=original.Select(i=>i.Target).Where(File.Exists).ToArray();
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(controller.Config.Items.SequenceEqual(original.Skip(1).Take(original.Count-2)),"批量删除只移除勾选项且保留其它项顺序");
                check(new ConfigStore().Load().Items.Select(i=>i.Target).SequenceEqual(controller.Config.Items.Select(i=>i.Target)),"批量删除保存到配置文件");
                check(diskBefore.All(File.Exists),"删除入口配置不删除目标文件");
            }
            controller.Config.Items=original.ToList();controller.Apply();window.RefreshPreview();window.UpdateLayout();window.SetDeleteMode(true);
            for(int i=0;i<original.Count;i++)Click(i);
            if(original.Count>0){check(window.DeleteSelectedItems() && controller.Config.Items.Count==0 && new ConfigStore().Load().Items.Count==0,"支持删除全部入口并保存空配置");}
        }
        finally {controller.Config.Items=original;controller.Apply();preview.SetDeleteMode(false);window.RefreshPreview();window.UpdateLayout();}
    }

    static async Task VerifyMultiGesture(AppController controller,string folder,Action<bool,string> check)
    {
        var original=controller.Config.Items;bool originalProtection=controller.Config.GameModeEnabled;
        var launched=new List<string>();controller.TestLaunch=i=>launched.Add(i.Name);
        controller.Config.Items=DemoSettings().Items;controller.Config.GameModeEnabled=true;controller.Apply();
        try
        {
            async Task Start()
            {controller.TestHoldStart();await Task.Delay(65);}
            void Point(double x,double y,int message=0x200)
            {var place=controller.TestWheel.Placement;controller.TestPointer(message,(int)Math.Round(place.X+x*place.Scale),(int)Math.Round(place.Y+y*place.Scale));}
            double radius=140;
            async Task Select(double x,double y)
            {Point(x,y);await Task.Delay(230);}
            await Start();await Select(-radius,0);Point(0,0);await Task.Delay(35);await Select(radius,0);
            check(launched.Count==0 && controller.TestWheel.Selection.Stops.Select(s=>s.Index).SequenceEqual(new[]{6,2}),"HoldingLeftThenRightCollectsWithoutAnyEarlyLaunch");
            check(!controller.TestWheel.DwellTimerRunning,"StableSelectionStopsDwellTimerWithoutEndingGesture");
            SaveVisual(controller.TestWheel,Path.Combine(folder,"多选路径-1.3.4.png"));
            Point(radius,0,0x202);await Task.Delay(160);controller.TestModifierUp();
            check(launched.Count==0 && controller.TestWheel.Selection.Stops.Count==0,"ReleaseInsideLastSectorCancelsEveryEntryAndPath");
            await Start();await Select(-radius,0);Point(0,0);await Task.Delay(35);await Select(radius,0);
            double outside=controller.TestWheel.Surface.OuterRadius+50;Point(outside,0);await Task.Delay(50);
            check(controller.TestWheel.Selection.Confirmed,"LastSectorOuterExtensionArmsBatch");
            check(!controller.TestWheel.DwellTimerRunning,"OuterConfirmationDoesNotKeepDwellTimerRunning");
            SaveVisual(controller.TestWheel,Path.Combine(folder,"外拉确认-1.3.4.png"));
            Point(outside,0,0x202);await Task.Delay(160);controller.TestModifierUp();
            check(launched.SequenceEqual(new[]{controller.Config.Items[6].Name,controller.Config.Items[2].Name}),"ConfirmedReleaseDispatchesBothEntriesInSelectionOrder");launched.Clear();
            await Start();await Select(radius,0);Point(outside,0);await Task.Delay(40);controller.TestEscape();await Task.Delay(80);Point(outside,0,0x202);await Task.Delay(120);controller.TestModifierUp();
            check(launched.Count==0 && controller.TestWheel.Selection.Stops.Count==0,"EscapeAfterOuterConfirmationClearsBatchAndReleaseCannotLaunch");
            await Start();await Select(radius,0);Point(outside,0);await Task.Delay(40);controller.TestModifierUp();await Task.Delay(80);Point(outside,0,0x202);await Task.Delay(120);
            check(launched.Count==0,"ModifierUpAfterOuterConfirmationCancelsWholeBatch");
            await Start();await Select(radius,0);Point(outside,0);await Task.Delay(40);Point(0,0,0x202);await Task.Delay(150);controller.TestModifierUp();
            check(launched.Count==0,"FinalReleasePositionIsUsedRatherThanPreviousConfirmedPosition");
            await Start();await Select(radius,0);Point(0,0);await Task.Delay(40);await Select(radius,0);Point(outside,0,0x202);await Task.Delay(150);controller.TestModifierUp();
            check(launched.Count==1,"RepeatedSelectionOnlyDispatchesOnce");launched.Clear();
            await Start();await Select(radius,0);Point(outside,0);await Task.Delay(40);controller.TestProtection("游戏保护：确认前切入游戏");Point(outside,0,0x202);await Task.Delay(150);controller.TestModifierUp();
            check(launched.Count==0,"GameProtectionCancelsEvenAnArmedBatch");controller.TestProtection(null);
            await Start();Point(-outside,0,0x202);await Task.Delay(150);controller.TestModifierUp();
            check(launched.SequenceEqual(new[]{controller.Config.Items[6].Name}),"FirstFastOutsideReleaseSelectsCurrentDirectionWithoutDwell");launched.Clear();
            await Start();await Select(radius,0);Point(-outside,0,0x202);await Task.Delay(150);controller.TestModifierUp();
            check(launched.Count==0,"ReleaseThroughUnselectedOppositeDirectionCannotLaunch");
            await VerifySelectedReturnGestures(controller,launched,check);
        }
        finally {controller.TestProtection(null);controller.Recover();controller.TestModifierUp();controller.Config.Items=original;controller.Config.GameModeEnabled=originalProtection;controller.Apply();controller.TestLaunch=null;}
    }
}
