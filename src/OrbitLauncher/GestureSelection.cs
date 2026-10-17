using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OrbitLauncher;

// A gesture only collects entries here. Launching is deferred until a confirmed release.
internal sealed class GestureSelection
{
    internal const int DwellMilliseconds=150;
    internal const double Extension=88;
    internal readonly record struct Stop(int Index,double X,double Y);
    readonly List<Stop> stops=new();
    readonly ReadOnlyCollection<Stop> stopView;
    ushort selectedMask;
    internal GestureSelection()=>stopView=stops.AsReadOnly();
    internal IReadOnlyList<Stop> Stops=>stopView;
    internal bool Contains(int index)=>index>=0 && index<16 && (selectedMask&(1<<index))!=0;
    internal bool NeedsDwell=>Candidate>=0 && Progress<1;
    internal int Hovered {get;private set;}=-1;
    internal int Armed {get;private set;}=-1;
    internal int Candidate {get;private set;}=-1;
    internal bool Confirmed {get;private set;}
    internal double X {get;private set;}
    internal double Y {get;private set;}
    internal double Progress {get;private set;}
    long entered;
    double radius,cancel;
    int count;
    internal void Begin(int itemCount,double outerRadius,double cancelRadius)
    { Clear();count=itemCount;radius=outerRadius;cancel=cancelRadius; }
    internal void Clear()
    { stops.Clear();selectedMask=0;Hovered=Armed=Candidate=-1;Confirmed=false;X=Y=Progress=0;count=0; }
    internal bool Update(double x,double y,long now)
    {
        var previous=(X,Y,Hovered,Armed,Candidate,Confirmed,Progress,stops.Count);
        UpdateCore(x,y,now);
        return previous!=(X,Y,Hovered,Armed,Candidate,Confirmed,Progress,stops.Count);
    }
    void UpdateCore(double x,double y,long now)
    {
        double previousX=X,previousY=Y;
        X=x;Y=y;double distance=Math.Sqrt(x*x+y*y);
        Hovered=RadialMath.Select(x,y,cancel,count);
        Confirmed=false;
        if(Hovered<0){Candidate=-1;Progress=0;return;}
        if(distance<=radius)
        {
            // Moving to another sector immediately disarms the previous extension.
            if(Hovered!=Armed)Armed=-1;
            if(Candidate!=Hovered){Candidate=Hovered;entered=now;}
            Progress=Math.Clamp((now-entered)/(double)DwellMilliseconds,0,1);
            if(Progress>=1)
            {
                Armed=Hovered;
                if(!Contains(Hovered)){stops.Add(new Stop(Hovered,x,y));selectedMask|=(ushort)(1<<Hovered);}
            }
        }
        else
        {
            // A first outward stroke can cross the whole ring between two samples,
            // or before its opening animation finishes. Infer that first choice
            // from the current ray; subsequent batch additions still require dwell.
            if(stops.Count==0)
            {
                double previousDistance=Math.Sqrt(previousX*previousX+previousY*previousY);
                bool usePrevious=previousDistance>cancel && previousDistance<=radius && RadialMath.Select(previousX,previousY,cancel,count)==Hovered;
                double stopRadius=(cancel+radius)/2;
                stops.Add(new Stop(Hovered,usePrevious?previousX:x/distance*stopRadius,usePrevious?previousY:y/distance*stopRadius));
                selectedMask|=(ushort)(1<<Hovered);Armed=Hovered;
            }
            Candidate=-1;Progress=0;
            // Once an entry is selected, leaving through a different unarmed
            // sector cannot silently add it to the batch.
            // The whole outward sector confirms, starting at the ring edge.
            // There is no separate button or extra radial target to aim for.
            Confirmed=Armed>=0 && Hovered==Armed;
            if(Hovered!=Armed)Armed=-1;
        }
    }
    internal int[] Release(bool commit)
    { var result=commit && Confirmed?stops.Select(s=>s.Index).ToArray():Array.Empty<int>();Clear();return result; }
}
