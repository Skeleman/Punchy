#ifndef PUNCHY_SHARED_CLOCK_INCLUDED
#define PUNCHY_SHARED_CLOCK_INCLUDED

// Seconds on a clock every player shares (the server's tick when playing together, otherwise time since start), set
// each frame by WorldSync. Ambient motion (fireflies, butterflies, motes, landmark effects) runs on it instead of
// _Time, so everyone sees the same critter in the same place without anything being sent.
float _PunchyClock;

#endif
