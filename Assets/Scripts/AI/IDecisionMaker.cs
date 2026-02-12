using UnityEngine;
using UnityEngine.UIElements;

public interface IDecisionMaker
{
    // interface for all AI entities 

    // 1.) getActions method for stride, fireball, open door, vs strike
    // all actions from action controller

    // 2.) getMovements
    // all the movement from action controller

    // 3.) getTarget 
    // who should i go for?
    // only enemy 
    // gameObject
    // go for player with low health or closest
    // return null - no one to target,
    // choose idle, or leave open door open for a "patrol" function
   
    // 4.) canHit function
    // given action, can i hit, are line of sight, range

    // 5.) decideAction
    // should i be moving unarmed strike, strike with sword, stride, putting on shield, fireball
    // generic action
    // get list of actions, can i hit anyone, if not, get the list of movement actions you can take



}
