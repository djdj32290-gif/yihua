namespace OrbitLauncher;

// Keyboard hooks report the transition before Windows updates its asynchronous key state.
internal sealed class ModifierState
{
    volatile bool alt,control,shift,rightAlt;
    public bool RightAlt=>rightAlt;
    public bool IsDown(int key)=>key switch { 0xA4=>alt,0xA2=>control,0xA0=>shift,0xA5=>rightAlt,_=>false };
    public void Set(int key,bool down)
    { switch(key) { case 0xA4:alt=down;break;case 0xA2:control=down;break;case 0xA0:shift=down;break;case 0xA5:rightAlt=down;break; } }
    public void Reset()=>alt=control=shift=rightAlt=false;
    public static uint Normalize(uint key,uint scan,uint flags)=>key switch
    {
        0x12=>(flags&1)!=0?0xA5u:0xA4u,
        0x11=>(flags&1)!=0?0xA3u:0xA2u,
        0x10=>scan==0x36?0xA1u:0xA0u,
        _=>key
    };
}
