using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace OrbitLauncher;
internal static partial class SelfTests
{
    static void VerifyFastSelection(Action<bool,string> check)
    {
        var selection=new GestureSelection();
        for(int count=1;count<=16;count++)for(int index=0;index<count;index++)
        {
            double angle=-Math.PI/2+index*2*Math.PI/count,x=450*Math.Cos(angle),y=450*Math.Sin(angle);
            selection.Begin(count,209,58);selection.Update(x,y,0);
            check(selection.Confirmed && selection.Stops.Count==1 && selection.Stops[0].Index==index,$"{count} 项 / {index} 第一帧已在圈外也正确选中");
            var node=selection.Stops[0];double nodeDistance=Math.Sqrt(node.X*node.X+node.Y*node.Y);
            check(nodeDistance>58 && nodeDistance<209,$"{count} 项 / {index} 稀疏输入的路径节点位于扇区内");
            check(selection.Release(true).SequenceEqual(new[]{index}),$"{count} 项 / {index} 首次快速外拉仅提交当前方向一次");
            selection.Begin(count,209,58);double other=angle+2*Math.PI/count;selection.Update(140*Math.Cos(other),140*Math.Sin(other),0);selection.Update(x,y,5);
            check(selection.Confirmed && selection.Release(true).SequenceEqual(new[]{index}),$"{count} 项 / {index} 首次外拉使用当前位置而非旧悬停方向");
        }
        selection.Begin(8,209,58);selection.Update(250,0,0);selection.Update(0,0,1);
        check(selection.Release(true).Length==0,"快速外拉后返回中心仍取消");
        selection.Begin(8,209,58);selection.Update(250,0,0);check(selection.Release(false).Length==0,"快速选择仍可被修饰键或 Esc 取消");
        selection.Begin(8,209,58);selection.Update(140,0,0);check(selection.Release(true).Length==0,"首次快速拖动仅在圈内松开不能启动");
        selection.Begin(8,209,58);selection.Update(-250,0,0);selection.Update(0,0,1);selection.Update(140,0,2);selection.Update(250,0,3);
        check(selection.Stops.Select(s=>s.Index).SequenceEqual(new[]{6}) && selection.Release(true).Length==0,"首项快速选择后其它入口仍需停留，不能误加入批量");
        selection.Begin(8,209,58);selection.Update(-250,0,0);selection.Update(0,0,1);selection.Update(140,0,2);selection.Update(140,0,152);selection.Update(250,0,153);
        check(selection.Release(true).SequenceEqual(new[]{6,2}),"首次直划可继续停留多选，最后外拉按顺序提交");
    }
    static async Task VerifyFastGestures(AppController controller,Action<bool,string> check)
    {
        var original=controller.Config.Items;var launched=new List<string>();controller.TestLaunch=item=>launched.Add(item.Name);
        controller.Config.Items=DemoSettings().Items;controller.Apply();
        var screen=Native.ScreenAt(400,400);int x=(screen.Work.Left+screen.Work.Right)/2,y=(screen.Work.Top+screen.Work.Bottom)/2;
        (int X,int Y) Point(double dx,double dy)=>(x+(int)Math.Round(dx*screen.Scale),y+(int)Math.Round(dy*screen.Scale));
        try
        {
            for(int index=0;index<8;index++)
            {
                double angle=-Math.PI/2+index*Math.PI/4;var p=Point(260*Math.Cos(angle),260*Math.Sin(angle));controller.TestModifierDown();
                controller.TestPointerBurst((0x201,x,y),(0x200,p.X,p.Y));await Task.Delay(45);
                check(launched.Count==0 && controller.TestWheel.Selection.Confirmed && controller.TestWheel.Selection.Stops.Single().Index==index,$"FastDirection{index}CorrectBeforeOpeningAnimationEndsAndNoEarlyLaunch");
                controller.TestPointer(0x202,p.X,p.Y);await Task.Delay(85);controller.TestModifierUp();
                check(launched.SequenceEqual(new[]{controller.Config.Items[index].Name}),$"FastDirection{index}ReleaseDispatchesExactlyOneCorrectEntry");launched.Clear();
            }
            var left=Point(-260,0);var right=Point(260,0);var nearLeft=Point(-40,0);var nearRight=Point(40,0);
            controller.TestModifierDown();
            // Deliberately prevent WPF from draining the queue between two complete
            // gestures. A global latest-position slot mixes their opposite targets.
            using(Dispatcher.CurrentDispatcher.DisableProcessing())
                controller.TestPointerBurst((0x201,x,y),(0x200,nearLeft.X,nearLeft.Y),(0x200,left.X,left.Y),(0x202,left.X,left.Y),
                    (0x201,x,y),(0x200,nearRight.X,nearRight.Y),(0x200,right.X,right.Y),(0x202,right.X,right.Y));
            await Task.Delay(150);controller.TestModifierUp();
            check(launched.SequenceEqual(new[]{controller.Config.Items[6].Name,controller.Config.Items[2].Name}),"QueuedRapidOppositeGesturesKeepIndependentPositionsAndLaunchOrder");launched.Clear();
            controller.TestModifierDown();
            controller.TestPointerBurst((0x201,x,y),(0x200,right.X,right.Y),(0x200,x,y),(0x202,x,y));await Task.Delay(130);controller.TestModifierUp();
            check(launched.Count==0,"FastGestureFinalCentreReleaseOverridesEarlierConfirmation");
            controller.TestModifierDown();controller.TestPointerBurst((0x201,x,y),(0x200,right.X,right.Y));controller.TestEscape();controller.TestPointer(0x202,right.X,right.Y);await Task.Delay(130);controller.TestModifierUp();
            check(launched.Count==0,"EscapeCancelsFastGestureEvenWhileOpeningEventsAreQueued");
            controller.TestModifierDown();controller.TestPointerBurst((0x201,x,y),(0x200,right.X,right.Y));controller.TestModifierUp();controller.TestPointer(0x202,right.X,right.Y);await Task.Delay(130);
            check(launched.Count==0,"ModifierReleaseCancelsFastGestureEvenWhileOpeningEventsAreQueued");
        }
        finally{controller.Recover();controller.TestModifierUp();controller.Config.Items=original;controller.Apply();controller.TestLaunch=null;}
    }
}
