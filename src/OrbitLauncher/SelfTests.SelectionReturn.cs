using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OrbitLauncher;
internal static partial class SelfTests
{
    static void VerifySelectedReturns(Action<bool,string> check)
    {
        var pair=new GestureSelection();pair.Begin(2,209,58);
        pair.Update(0,-140,0);pair.Update(0,-140,150);
        pair.Update(0,140,160);pair.Update(0,140,310);pair.Update(0,260,320);
        check(pair.Confirmed && pair.Armed==1 && pair.Stops.Select(s=>s.Index).SequenceEqual(new[]{0,1}),
            "TwoEntryWheelSelectsTopThenBottomAndConfirmsBottom");
        long pairTime=320;
        foreach(int index in new[]{0,1,0,1})
        {
            double sign=index==0?-1:1;
            pair.Update(0,sign*140,++pairTime);pair.Update(0,sign*260,++pairTime);
            check(pair.Confirmed && pair.Armed==index && !pair.NeedsDwell && pair.Stops.Select(s=>s.Index).SequenceEqual(new[]{0,1}),
                "TwoEntryWheelReturnsImmediatelyWithoutDuplicateSelection"+pairTime);
        }
        check(pair.Release(true).SequenceEqual(new[]{0,1}) && pair.Release(true).Length==0,
            "TwoEntryWheelReturnedPairCommitsOriginalOrderExactlyOnce");

        var selection=new GestureSelection();
        void SelectPair()
        {
            selection.Begin(8,209,58);
            selection.Update(0,-140,0);selection.Update(0,-140,150);
            selection.Update(0,140,160);selection.Update(0,140,310);
        }
        SelectPair();selection.Update(0,260,320);
        check(selection.Confirmed && selection.Armed==4,"SelectedBottomInitiallyConfirmsPair");
        long now=320;
        foreach(int index in new[]{0,4,0,4})
        {
            double sign=index==0?-1:1;
            selection.Update(0,sign*140,++now);
            check(!selection.Confirmed,"ReturningInsideSelectedSectorWithdrawsConfirmation"+now);
            selection.Update(0,sign*260,++now);
            check(selection.Confirmed && selection.Armed==index && !selection.NeedsDwell,
                "SelectedSectorReturnImmediatelyConfirmsWithoutDwell"+now);
            check(selection.Stops.Select(s=>s.Index).SequenceEqual(new[]{0,4}),
                "SelectedSectorReturnKeepsUniqueOriginalSelectionOrder"+now);
        }
        selection.Update(260,0,++now);
        check(!selection.Confirmed && selection.Armed==-1 && selection.Stops.Count==2,
            "UnselectedOutsideDirectionCannotConfirmOrAddToReturnedPair");
        selection.Update(0,-260,++now);
        check(selection.Confirmed && selection.Armed==0,"SelectedReturnRecoversAfterUnselectedOutsideDirection");
        selection.Update(0,0,++now);
        check(!selection.Confirmed,"CentreStillWithdrawsReturnedConfirmation");
        selection.Update(0,260,++now);
        check(selection.Confirmed && selection.Armed==4,"SelectedReturnRecoversImmediatelyAfterCentre");
        check(selection.Release(true).SequenceEqual(new[]{0,4}) && selection.Release(true).Length==0,
            "ReturnedPairCommitsOriginalOrderExactlyOnce");
        foreach(int index in new[]{0,4})
        {
            SelectPair();selection.Update(260,0,320);selection.Update(0,index==0?-260:260,321);
            check(selection.Confirmed && selection.Release(true).SequenceEqual(new[]{0,4}),
                "EitherPreviouslySelectedOutsideDirectionCanCommitPair"+index);
        }
        foreach(bool centre in new[]{false,true})
        {
            SelectPair();selection.Update(0,260,320);selection.Update(0,-260,321);
            selection.Update(0,centre?0:-140,322);
            check(selection.Release(true).Length==0 && selection.Stops.Count==0,
                centre?"CentreReleaseCancelsReturnedPair":"InsideReleaseCancelsReturnedPair");
        }
        SelectPair();selection.Update(0,260,320);selection.Update(0,-260,321);
        check(selection.Release(false).Length==0 && selection.Stops.Count==0,
            "ExplicitCancelStillClearsReturnedConfirmedPair");

        // The outer effect is drawn from Armed as well as Confirmed. Inspect
        // its pixels so restoring logical confirmation cannot leave a stale ray.
        const int size=632;
        var surface=new RadialSurface {Width=size,Height=size};surface.Configure(DemoSettings());
        surface.Measure(new Size(size,size));surface.Arrange(new Rect(0,0,size,size));surface.UpdateLayout();
        var visual=surface.Gesture;visual.Begin(8,surface.OuterRadius,58);
        visual.Update(0,-140,0);visual.Update(0,-140,150);visual.Update(0,140,160);visual.Update(0,140,310);
        visual.Update(0,surface.OuterRadius+50,320);
        foreach(int index in new[]{0,4})
        {
            double sign=index==0?-1:1;
            visual.Update(0,sign*140,++now);visual.Update(0,sign*(surface.OuterRadius+50),++now);
            surface.InvalidateVisual();surface.UpdateLayout();
            var bitmap=new RenderTargetBitmap(size*2,size*2,192,192,PixelFormats.Pbgra32);bitmap.Render(surface);
            var pixels=new byte[size*size*16];int stride=size*8;bitmap.CopyPixels(pixels,stride,0);
            byte Alpha(int direction)
            {
                double angle=-Math.PI/2+direction*Math.PI/4+.18,r=surface.OuterRadius+12;
                int x=(int)Math.Round((size/2d+r*Math.Cos(angle))*2),y=(int)Math.Round((size/2d+r*Math.Sin(angle))*2);
                return pixels[y*stride+x*4+3];
            }
            check(visual.Confirmed && visual.Armed==index && Alpha(index)>90 && Alpha(index==0?4:0)<40,
                "ReturningToSelectedSectorMovesVisibleConfirmationEffect"+index);
        }
    }

    static async Task VerifySelectedReturnGestures(AppController controller,List<string> launched,Action<bool,string> check)
    {
        async Task Drain()=>await Dispatcher.Yield(DispatcherPriority.Background);
        void Point(double x,double y,int message=0x200)
        {
            var place=controller.TestWheel.Placement;
            controller.TestPointer(message,(int)Math.Round(place.X+x*place.Scale),(int)Math.Round(place.Y+y*place.Scale));
        }
        double outside=0;
        async Task StartPair()
        {
            var screen=Native.ScreenAt(400,400);
            int ox=(screen.Work.Left+screen.Work.Right)/2,oy=(screen.Work.Top+screen.Work.Bottom)/2;
            controller.TestModifierDown();
            controller.TestPointerBurst((0x201,ox,oy),(0x200,ox,oy-(int)Math.Round(140*screen.Scale)));await Drain();
            foreach(int index in new[]{0,4})
            {
                Point(0,index==0?-140:140);await Drain();
                await Task.Delay(GestureSelection.DwellMilliseconds+80);await Drain();
                check(controller.TestWheel.Selection.Contains(index),"SelectedReturnInitialDwellAddsEntry"+index);
            }
            outside=controller.TestWheel.Surface.OuterRadius+50;
            Point(0,outside);await Drain();
            check(controller.TestWheel.Selection.Confirmed,"SelectedReturnPairStartsConfirmedAtBottom");
        }
        async Task Return(int index,string stage)
        {
            var place=controller.TestWheel.Placement;double sign=index==0?-1:1;
            int px=(int)Math.Round(place.X),inner=(int)Math.Round(place.Y+sign*140*place.Scale),outer=(int)Math.Round(place.Y+sign*outside*place.Scale);
            // Both samples are supplied in one input-worker operation. There is
            // no dwell or animation delay before checking the returned direction.
            controller.TestPointerBurst((0x200,px,inner),(0x200,px,outer));await Drain();
            var wheel=controller.TestWheel;var selection=wheel.Selection;
            check(wheel.IsVisible && selection.Confirmed && selection.Armed==index && wheel.Surface.Selected==index && !wheel.DwellTimerRunning,
                stage+"SelectedReturnRestoresConfirmationAndUiDirectionImmediately");
            check(launched.Count==0 && selection.Stops.Select(s=>s.Index).SequenceEqual(new[]{0,4}),
                stage+"SelectedReturnDoesNotDuplicateOrLaunchEntries");
        }
        launched.Clear();await StartPair();
        int turn=0;foreach(int index in new[]{0,4,0,4})await Return(index,"RoundTrip"+(++turn));
        Point(outside,0);await Drain();
        check(!controller.TestWheel.Selection.Confirmed && controller.TestWheel.Selection.Armed==-1 && launched.Count==0,
            "ReturnedPairUnselectedDirectionClearsUiConfirmation");
        await Return(0,"AfterUnselectedDirection");
        Point(0,-outside,0x202);await Drain();controller.TestModifierUp();
        check(launched.SequenceEqual(new[]{controller.Config.Items[0].Name,controller.Config.Items[4].Name}) && controller.GesturePhase==GesturePhase.Idle,
            "ReturnedPairMouseReleaseLaunchesTwoOriginalEntriesExactlyOnceInOrder");
        launched.Clear();
        foreach(string cancel in new[]{"Inside","Centre","AltUp","Escape"})
        {
            await StartPair();await Return(0,cancel+"Top");await Return(4,cancel+"Bottom");
            if(cancel=="AltUp")controller.TestModifierUp();
            else if(cancel=="Escape")controller.TestEscape();
            Point(0,cancel=="Inside"?140:cancel=="Centre"?0:outside,0x202);await Drain();controller.TestModifierUp();
            check(launched.Count==0 && controller.GesturePhase==GesturePhase.Idle && controller.TestWheel.Selection.Stops.Count==0 && !controller.TestWheel.Selection.Confirmed,
                cancel+"StillCancelsEntireReturnedPair");
        }
    }
}
