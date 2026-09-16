/**
 * AiShotLibrary.ts
 * 
 * Production broadcast shot catalog categorized by Event Type & Camera Role.
 * Supports:
 * - Live Music Concert / Festival
 * - Church / Worship Service
 * - Sports Match Broadcast
 * - Corporate Keynote / Conference
 * - Wedding Ceremony & Reception
 * 
 * Provides composition rules, focal lengths, camera movements,
 * viewfinder layout styles, and realistic broadcast director speech scripts.
 */

import { CameraRole, EventType } from '../context/SettingsContext';
import { ShotSuggestion } from './DirectorSocketService';
import { COMPLETE_SHOT_CATALOG } from './ShotDatabase';

export type ViewfinderType =
  | 'low-angle-push'
  | 'dutch-solo'
  | 'stage-sweep'
  | 'tight-portrait'
  | 'two-shot'
  | 'foh-wide'
  | 'artist-orbit'
  | 'crane-swoop'
  | 'crowd-sweep'
  | 'drummer-hero';

export interface AiShotDefinition {
  id: string;
  role: CameraRole;
  eventType: EventType;
  title: string;
  description: string;
  category: string;
  focalLength: string;
  movement: string;
  composition: string;
  viewfinderType: ViewfinderType;
  voiceScript: string;
  durationSeconds: number;
}

export const AI_SHOT_CATALOG_BY_EVENT: Record<EventType, Record<CameraRole, AiShotDefinition[]>> = {
  'Concert': {
    'Roving Stage': [
      {
        id: 'rov-1',
        role: 'Roving Stage',
        eventType: 'Concert',
        title: 'Low-Angle Push • Lead Vocalist',
        description: 'Crouch 1m off stage floor, tilt up 25°, slow push toward lead singer during vocal build.',
        category: 'STAGE DYNAMIC',
        focalLength: '24mm Wide',
        movement: 'Slow Push-In',
        composition: 'Singer eyes aligned on top-third line, stage lights backlight flare.',
        viewfinderType: 'low-angle-push',
        voiceScript: 'Camera 1 roving, get down on stage floor and push in slow on the lead singer... stand by... push now!',
        durationSeconds: 20,
      },
      {
        id: 'rov-2',
        role: 'Roving Stage',
        eventType: 'Concert',
        title: 'Dutch Angle Riff • Guitar Solo',
        description: 'Tilt camera 15° Dutch roll stage right, frame guitar neck entering bottom-left quadrant.',
        category: 'HIGH ENERGY',
        focalLength: '35mm Prime',
        movement: 'Slight Whip Pan',
        composition: 'Diagonal guitar neck dynamic line, guitarist fretboard in pin-sharp focus.',
        viewfinderType: 'dutch-solo',
        voiceScript: 'Ready 1 on lead guitar, give me that Dutch tilt on the fretboard... and take 1!',
        durationSeconds: 18,
      },
      {
        id: 'rov-3',
        role: 'Roving Stage',
        eventType: 'Concert',
        title: 'Stage Edge Sweep • Audience Flares',
        description: 'Move along downstage lip, capture singer with cheering crowd and phone lights in foreground.',
        category: 'ATMOSPHERE',
        focalLength: '20mm Ultra-Wide',
        movement: 'Lateral Tracking Pan',
        composition: 'Silhouette hands in lower 20%, stage performers commanding center.',
        viewfinderType: 'stage-sweep',
        voiceScript: 'Cam 1, sweep stage left along the monitors, catch the front row hands... hold that shot!',
        durationSeconds: 22,
      },
      {
        id: 'rov-4',
        role: 'Roving Stage',
        eventType: 'Concert',
        title: 'Drummer Hero • Kick & Cymbal Crash',
        description: 'Behind drum kit stage right, low angle past ride cymbal focused on drummer cadence.',
        category: 'RHYTHM HERO',
        focalLength: '50mm Telephoto',
        movement: 'Static Punchy Framing',
        composition: 'Cymbal edge foreground blur, drummer face centered with dynamic motion.',
        viewfinderType: 'drummer-hero',
        voiceScript: 'Camera 1, find the drummer before the bridge drop... stand by drummer... take 1!',
        durationSeconds: 16,
      },
    ],
    'FOH Wide': [
      {
        id: 'foh-1',
        role: 'FOH Wide',
        eventType: 'Concert',
        title: 'Master Arena Overview • Full Production',
        description: 'Full stage frame showing total lighting rig, side LED walls, and complete band footprint.',
        category: 'MASTER WIDE',
        focalLength: '18mm Master Wide',
        movement: 'Static Locked Off',
        composition: 'Horizon leveled, stage centerline exactly centered on crosshair grid.',
        viewfinderType: 'foh-wide',
        voiceScript: 'Camera 2 FOH, lock off the master wide, lights coming up full arena... ready 2, take 2!',
        durationSeconds: 25,
      },
      {
        id: 'foh-2',
        role: 'FOH Wide',
        eventType: 'Concert',
        title: 'Lighting Blackout Hold • Pyro Prep',
        description: 'Hold steady master frame during blackout for impending stage pyro and beam explosion.',
        category: 'SFX TIMING',
        focalLength: '24mm Wide',
        movement: 'Rock Solid Hold',
        composition: 'Leave 25% upper headroom for pyro sparks and beam towers.',
        viewfinderType: 'foh-wide',
        voiceScript: 'Two, hold the wide through the blackout! Pyro cues in 3, 2, 1... hold two!',
        durationSeconds: 20,
      },
    ],
    'Host Close-Up': [
      {
        id: 'host-1',
        role: 'Host Close-Up',
        eventType: 'Concert',
        title: 'Tight Eye-Line Portrait • Lead Vocalist',
        description: 'Bust shot from chest to top of hair. Keep eye-line precisely on the upper third grid.',
        category: 'PORTRAIT TIGHT',
        focalLength: '85mm Portrait',
        movement: 'Smooth Breathing Float',
        composition: '5% headroom, subject looking slightly screen-left toward audience center.',
        viewfinderType: 'tight-portrait',
        voiceScript: 'Camera 3, tight on the vocalist, hold that clean headroom... ready 3, take 3!',
        durationSeconds: 24,
      },
      {
        id: 'host-2',
        role: 'Host Close-Up',
        eventType: 'Concert',
        title: 'Over-The-Shoulder Two-Shot • Duet',
        description: 'Frame guitarist shoulder in left foreground, guest vocalist in clear upper-right quadrant.',
        category: 'DUET PROFILE',
        focalLength: '70mm Medium Tele',
        movement: 'Micro-Adjust Framing',
        composition: 'Golden ratio framing on vocalist expressions, shallow depth of field.',
        viewfinderType: 'two-shot',
        voiceScript: 'Three, punch in for the duet over the guitar shoulder... lock that in, take 3!',
        durationSeconds: 20,
      },
    ],
    'Steadicam': [
      {
        id: 'stedi-1',
        role: 'Steadicam',
        eventType: 'Concert',
        title: '360° Artist Orbit • Center Stage',
        description: 'Continuous circular orbit around performer during vocal crescendo. Maintain 2m distance.',
        category: 'KINETIC ORBIT',
        focalLength: '28mm Gimbal',
        movement: 'Smooth Clockwise Orbit',
        composition: 'Artist pinned dead-center, stage lighting sweeps continuously behind.',
        viewfinderType: 'artist-orbit',
        voiceScript: 'Steadicam, start your 360 orbit around the singer now... keep it smooth... looking great!',
        durationSeconds: 24,
      },
    ],
    'Jib / Crane': [
      {
        id: 'jib-1',
        role: 'Jib / Crane',
        eventType: 'Concert',
        title: 'Arena High Swoop • Dramatic Plunge',
        description: 'Ascend to 8m arena ceiling, then slow majestic swoop down toward downstage center.',
        category: 'AERIAL PLUNGE',
        focalLength: '16mm Ultra-Wide',
        movement: 'Vertical Crane Descent',
        composition: 'Vast crowd sea in foreground transitioning to performer hero reveal.',
        viewfinderType: 'crane-swoop',
        voiceScript: 'Jib, give me the big arena swoop from the roof down to stage... rolling Jib... take Jib!',
        durationSeconds: 22,
      },
    ],
    'Audience Reaction': [
      {
        id: 'aud-1',
        role: 'Audience Reaction',
        eventType: 'Concert',
        title: 'Sing-Along Emotional Fan • Front Row',
        description: 'Isolate energized singing audience member in front pit. Tight emotional framing.',
        category: 'CROWD PASSION',
        focalLength: '135mm Telephoto',
        movement: 'Quick Punch & Hold',
        composition: 'Shallow focus isolating singing fan against blurred stage backlights.',
        viewfinderType: 'crowd-sweep',
        voiceScript: 'Cam 4, find me that fan singing in the front row... yes right there! Take 4!',
        durationSeconds: 18,
      },
    ],
  },

  'Worship': {
    'Roving Stage': [
      {
        id: 'wor-rov-1',
        role: 'Roving Stage',
        eventType: 'Worship',
        title: 'Reverent Push • Worship Leader & Piano',
        description: 'Slow, peaceful push from stage left toward worship leader at keyboard during acoustic prayer.',
        category: 'REVERENT ACOUSTIC',
        focalLength: '35mm Gentle Prime',
        movement: 'Gliding Slow Push',
        composition: 'Soft warm backlight, hands on piano keys in lower third, worship leader framed right.',
        viewfinderType: 'low-angle-push',
        voiceScript: 'Camera 1 roving, slow reverent push on the worship leader at the piano... hold that gentle framing.',
        durationSeconds: 25,
      },
      {
        id: 'wor-rov-2',
        role: 'Roving Stage',
        eventType: 'Worship',
        title: 'Acoustic Guitar Nuance • Fingerpicking',
        description: 'Low angle near monitor wedge, focusing on guitar body and acoustic cadence during bridge.',
        category: 'INTIMATE INSTRUMENT',
        focalLength: '50mm Prime',
        movement: 'Subtle Breathing Float',
        composition: 'Warm stage illumination, soft focus falloff on sanctuary background.',
        viewfinderType: 'dutch-solo',
        voiceScript: 'One, settle on the acoustic guitar for the chorus build... nice and steady... take 1.',
        durationSeconds: 20,
      },
    ],
    'FOH Wide': [
      {
        id: 'wor-foh-1',
        role: 'FOH Wide',
        eventType: 'Worship',
        title: 'Sanctuary Full Wide • Congregation Worship',
        description: 'Elevated wide framing entire altar, cross illumination, and congregation standing in worship.',
        category: 'SANCTUARY MAJESTY',
        focalLength: '20mm Architecture Wide',
        movement: 'Locked Off Rock Solid',
        composition: 'Architectural symmetry, altar center, congregation silhouette in lower third.',
        viewfinderType: 'foh-wide',
        voiceScript: 'Two FOH, hold the majestic wide of the sanctuary and congregation... looking beautiful, hold two.',
        durationSeconds: 30,
      },
    ],
    'Host Close-Up': [
      {
        id: 'wor-host-1',
        role: 'Host Close-Up',
        eventType: 'Worship',
        title: 'Pastor Sermon • Tight Eye-Line Portrait',
        description: 'Chest-up bust framing on pastor at pulpit. Lock eye-line with center sanctuary congregation.',
        category: 'PULPIT PORTRAIT',
        focalLength: '85mm Portrait',
        movement: 'Micro-Tracking Pivot',
        composition: 'Clean 8% headroom, bible on lectern framed subtly in bottom margin.',
        viewfinderType: 'tight-portrait',
        voiceScript: 'Three, tight on the pastor at the pulpit, follow their delivery smoothly... ready 3, take 3.',
        durationSeconds: 25,
      },
    ],
    'Steadicam': [
      {
        id: 'wor-stedi-1',
        role: 'Steadicam',
        eventType: 'Worship',
        title: 'Center Aisle Gliding Push • Altar Reveal',
        description: 'Slow graceful glide down center aisle toward stage as congregation sings chorus.',
        category: 'GRACEFUL GLIDE',
        focalLength: '24mm Stabilized',
        movement: 'Slow Forward Walk',
        composition: 'Pews on lateral borders creating natural leading lines toward stage cross.',
        viewfinderType: 'artist-orbit',
        voiceScript: 'Steadi, slow graceful glide down center aisle... take your time, keep it reverent... take Steadi.',
        durationSeconds: 28,
      },
    ],
    'Jib / Crane': [
      {
        id: 'wor-jib-1',
        role: 'Jib / Crane',
        eventType: 'Worship',
        title: 'Balcony Sanctuary Descent • Altar Cross',
        description: 'Slow descending arc starting from sanctuary high ceiling down toward choir loft.',
        category: 'AERIAL BLESSING',
        focalLength: '18mm Wide',
        movement: 'Smooth Crane Float',
        composition: 'Overhead view of congregation hands uplifted transitioning to choir.',
        viewfinderType: 'crane-swoop',
        voiceScript: 'Jib, gentle descent from the ceiling over the congregation... nice and slow... ready Jib, take Jib.',
        durationSeconds: 26,
      },
    ],
    'Audience Reaction': [
      {
        id: 'wor-aud-1',
        role: 'Audience Reaction',
        eventType: 'Worship',
        title: 'Congregation Uplifted Hands • Silhouette',
        description: 'Frame worshippers with raised hands in silhouette against stage lighting glow.',
        category: 'WORSHIP RESPONSE',
        focalLength: '70mm Medium Tele',
        movement: 'Slow Horizon Drift',
        composition: 'Raised hands in lower half, warm stage light spilling between silhouettes.',
        viewfinderType: 'crowd-sweep',
        voiceScript: 'Four, find the congregation worshipping with raised hands... beautiful silhouette, hold four.',
        durationSeconds: 22,
      },
    ],
  },

  'Sports': {
    'Roving Stage': [
      {
        id: 'spo-rov-1',
        role: 'Roving Stage',
        eventType: 'Sports',
        title: 'Sideline Hero • Player Focus & Intensity',
        description: 'Sideline low angle tracking star player getting instructions before entering play.',
        category: 'SIDELINE INTENSITY',
        focalLength: '70mm Sports Tele',
        movement: 'Fast Snappy Refocus',
        composition: 'Player profile looking right, blurred background turf/court lights.',
        viewfinderType: 'low-angle-push',
        voiceScript: 'Camera 1 roving, tight on number 10 on the sideline, catch their focus... ready 1, take 1!',
        durationSeconds: 15,
      },
      {
        id: 'spo-rov-2',
        role: 'Roving Stage',
        eventType: 'Sports',
        title: 'Coach Passionate Reaction • Tactical Call',
        description: 'Tight framing on head coach shouting tactical instructions from technical area.',
        category: 'COACH REACTION',
        focalLength: '85mm Fast Tele',
        movement: 'Pivoting Track',
        composition: 'Coach framed in left third, bench assistant coaches blurred right.',
        viewfinderType: 'dutch-solo',
        voiceScript: 'One, coach is furious with the referee call! Grab the coach reaction now! Take 1!',
        durationSeconds: 16,
      },
    ],
    'FOH Wide': [
      {
        id: 'spo-foh-1',
        role: 'FOH Wide',
        eventType: 'Sports',
        title: 'Tactical Full Field • Formation Overview',
        description: 'Elevated midfield master wide capturing both offensive and defensive team formations.',
        category: 'TACTICAL MASTER',
        focalLength: '24mm High Master',
        movement: 'Fluid Lateral Pan',
        composition: 'Field boundaries aligned with safe action box, ball carrier centered.',
        viewfinderType: 'foh-wide',
        voiceScript: 'Two FOH, track the ball movement across midfield, stay wide for the counterattack... take 2!',
        durationSeconds: 20,
      },
    ],
    'Host Close-Up': [
      {
        id: 'spo-host-1',
        role: 'Host Close-Up',
        eventType: 'Sports',
        title: 'Studio Anchor Desk • Post-Match Breakdown',
        description: 'Tight broadcast studio framing on sports analyst delivering key play breakdown.',
        category: 'STUDIO DESK',
        focalLength: '85mm Broadcast',
        movement: 'Locked Anchor Framing',
        composition: 'Graphics lower-third safe, anchor shoulders squared to camera.',
        viewfinderType: 'tight-portrait',
        voiceScript: 'Camera 3, tight on the analyst for the replay breakdown in 3, 2, 1... take 3!',
        durationSeconds: 22,
      },
    ],
    'Steadicam': [
      {
        id: 'spo-stedi-1',
        role: 'Steadicam',
        eventType: 'Sports',
        title: 'Player Tunnel Walkout • Match Entrance',
        description: 'Walk backward ahead of team captains exiting locker room tunnel onto the pitch.',
        category: 'TUNNEL WALKOUT',
        focalLength: '20mm Ultra-Wide',
        movement: 'Reverse Tracking Run',
        composition: 'Captains centered, tunnel flare transitioning to stadium roar.',
        viewfinderType: 'artist-orbit',
        voiceScript: 'Steadicam, lead the team out of the tunnel! Stay steady on the captain... take Steadi!',
        durationSeconds: 20,
      },
    ],
    'Jib / Crane': [
      {
        id: 'spo-jib-1',
        role: 'Jib / Crane',
        eventType: 'Sports',
        title: 'Goal Post Swoop • Corner Kick Arc',
        description: 'High crane position swooping down toward goal crossbar as corner kick is delivered.',
        category: 'AERIAL GOAL',
        focalLength: '18mm Wide',
        movement: 'Curved Plunge',
        composition: 'Penalty box crowd action framed from above, high drama angle.',
        viewfinderType: 'crane-swoop',
        voiceScript: 'Jib, swoop over the crossbar for the corner kick... ball in the air... take Jib!',
        durationSeconds: 18,
      },
    ],
    'Audience Reaction': [
      {
        id: 'spo-aud-1',
        role: 'Audience Reaction',
        eventType: 'Sports',
        title: 'Ultras Fan Section • Chanting Frenzy',
        description: 'Kinetic framing on passionate fan section jumping and waving club flags.',
        category: 'FAN PASSION',
        focalLength: '100mm Telephoto',
        movement: 'Rhythmic Bounce Pan',
        composition: 'Wall of colors and shouting faces filling 100% of the frame.',
        viewfinderType: 'crowd-sweep',
        voiceScript: 'Four, find the supporters chanting behind the goal! Look at that energy, take 4!',
        durationSeconds: 16,
      },
    ],
  },

  'Corporate': {
    'Roving Stage': [
      {
        id: 'corp-rov-1',
        role: 'Roving Stage',
        eventType: 'Corporate',
        title: 'Executive Walk & Talk • Stage Tracking',
        description: 'Smooth low-angle lateral glide tracking keynote speaker crossing main presentation stage.',
        category: 'EXECUTIVE TRACK',
        focalLength: '35mm Clean Prime',
        movement: 'Lateral Dolly Pace',
        composition: 'Speaker in leading third with stage graphic screen visible behind.',
        viewfinderType: 'stage-sweep',
        voiceScript: 'Camera 1 roving, walk with the CEO as they move to stage left... smooth and professional... take 1.',
        durationSeconds: 22,
      },
    ],
    'FOH Wide': [
      {
        id: 'corp-foh-1',
        role: 'FOH Wide',
        eventType: 'Corporate',
        title: 'Auditorium Main Stage • Dual LED Screens',
        description: 'Balanced wide overview of executive presenter flanked by high-res keynote slide displays.',
        category: 'KEYNOTE MASTER',
        focalLength: '24mm Precision Wide',
        movement: 'Locked Off Pristine',
        composition: 'Speaker centered between large LED graphics, perfectly level horizon.',
        viewfinderType: 'foh-wide',
        voiceScript: 'Two FOH, lock the wide showing the keynote slides and presenter... ready 2, take 2.',
        durationSeconds: 25,
      },
    ],
    'Host Close-Up': [
      {
        id: 'corp-host-1',
        role: 'Host Close-Up',
        eventType: 'Corporate',
        title: 'Speaker Podium Bust • Crisp Eye-Line',
        description: 'Professional head-and-shoulders framing on presenter, eye-line with confidence monitor.',
        category: 'KEYNOTE BUST',
        focalLength: '85mm Razor Sharp',
        movement: 'Subtle Micro-Refocus',
        composition: '10% headroom, lapel microphone centered, crisp neutral corporate background.',
        viewfinderType: 'tight-portrait',
        voiceScript: 'Camera 3, tight on the speaker for the product announcement... lock eye-line... take 3.',
        durationSeconds: 24,
      },
    ],
    'Steadicam': [
      {
        id: 'corp-stedi-1',
        role: 'Steadicam',
        eventType: 'Corporate',
        title: 'Product Pedestal Orbit • Hardware Reveal',
        description: 'Smooth 360° circular orbit around new product pedestal under spotlight illumination.',
        category: 'PRODUCT REVEAL',
        focalLength: '28mm Macro Gimbal',
        movement: 'Precision Orbit',
        composition: 'Product centered in golden ratio, specular lighting gleam across chassis.',
        viewfinderType: 'artist-orbit',
        voiceScript: 'Steadicam, begin slow orbit around the prototype on the pedestal... hold focus, take Steadi.',
        durationSeconds: 20,
      },
    ],
    'Jib / Crane': [
      {
        id: 'corp-jib-1',
        role: 'Jib / Crane',
        eventType: 'Corporate',
        title: 'Grand Hall Ceiling Rise • Keynote Opening',
        description: 'Ascend over thousands of attendees to reveal vast conference hall scale and stage.',
        category: 'CORPORATE SCALE',
        focalLength: '18mm Wide',
        movement: 'Majestic Vertical Rise',
        composition: 'Convention hall symmetry, corporate branding banners in upper quadrants.',
        viewfinderType: 'crane-swoop',
        voiceScript: 'Jib, rise to the rafters as the intro video finishes... big reveal... take Jib!',
        durationSeconds: 25,
      },
    ],
    'Audience Reaction': [
      {
        id: 'corp-aud-1',
        role: 'Audience Reaction',
        eventType: 'Corporate',
        title: 'Audience Q&A Attendee • Microphone Stand',
        description: 'Medium portrait on conference delegate asking question at aisle standing microphone.',
        category: 'Q&A ATTENDEE',
        focalLength: '70mm Telephoto',
        movement: 'Rapid Settle & Lock',
        composition: 'Delegate profile framed right, listening auditorium attendees in soft focus.',
        viewfinderType: 'two-shot',
        voiceScript: 'Four, find microphone 2 in the aisle for the next question... locked on attendee, take 4.',
        durationSeconds: 20,
      },
    ],
  },

  'Wedding': {
    'Roving Stage': [
      {
        id: 'wed-rov-1',
        role: 'Roving Stage',
        eventType: 'Wedding',
        title: 'Groom Emotional Tear • Bride Entrance',
        description: 'Low-profile positioning downstage right, capturing groom reaction as bridal march plays.',
        category: 'EMOTIONAL MOMENT',
        focalLength: '85mm f/1.4 Creamy',
        movement: 'Discreet Steady Hold',
        composition: 'Groom eyes filled with emotion, best man blurred slightly in soft background.',
        viewfinderType: 'tight-portrait',
        voiceScript: 'Camera 1 roving, stay low and quiet, get the groom reaction as she walks in... beautiful, take 1.',
        durationSeconds: 22,
      },
      {
        id: 'wed-rov-2',
        role: 'Roving Stage',
        eventType: 'Wedding',
        title: 'Exchange of Rings • Macro Hands Framing',
        description: 'Tight framing on couple hands exchanging gold rings over bridal floral bouquet.',
        category: 'RING CEREMONY',
        focalLength: '105mm Macro',
        movement: 'Gentle Breathe Focus',
        composition: 'Rings in pin-sharp focus, lace dress detail, warm ambient sunlight.',
        viewfinderType: 'two-shot',
        voiceScript: 'One, push in tight on the hands for the ring exchange... steady hands... take 1.',
        durationSeconds: 18,
      },
    ],
    'FOH Wide': [
      {
        id: 'wed-foh-1',
        role: 'FOH Wide',
        eventType: 'Wedding',
        title: 'Altar Architecture Wide • Vows Exchange',
        description: 'Symmetrical church nave wide framing stained glass, floral arch, and entire bridal party.',
        category: 'CEREMONY WIDE',
        focalLength: '24mm Architectural',
        movement: 'Locked Off Romantic',
        composition: 'Couple at altar centered under floral archway, guests filling pews in lower third.',
        viewfinderType: 'foh-wide',
        voiceScript: 'Two FOH, hold the romantic ceremony wide under the floral arch... hold two.',
        durationSeconds: 30,
      },
    ],
    'Host Close-Up': [
      {
        id: 'wed-host-1',
        role: 'Host Close-Up',
        eventType: 'Wedding',
        title: 'Bride Romantic Vows • Intimate Profile',
        description: 'Three-quarter profile on bride smiling through tears while speaking personal vows.',
        category: 'VOWS CLOSE-UP',
        focalLength: '85mm Portrait Prime',
        movement: 'Gentle Floating Hold',
        composition: 'Veil detail backlit by sun, soft bridal bouquet in bottom corner.',
        viewfinderType: 'tight-portrait',
        voiceScript: 'Three, tight on the bride as she reads her vows... hold that intimate portrait, take 3.',
        durationSeconds: 26,
      },
    ],
    'Steadicam': [
      {
        id: 'wed-stedi-1',
        role: 'Steadicam',
        eventType: 'Wedding',
        title: 'First Dance 360° Orbit • Reception',
        description: 'Smooth romantic circular orbit around newlyweds during their spotlight first dance.',
        category: 'FIRST DANCE',
        focalLength: '24mm Gimbal Glow',
        movement: 'Slow Velvet Orbit',
        composition: 'Couple centered in dance hold, fairy lights creating circular bokeh streaks behind.',
        viewfinderType: 'artist-orbit',
        voiceScript: 'Steadicam, glide into the first dance orbit... slow and romantic... beautiful, take Steadi.',
        durationSeconds: 28,
      },
    ],
    'Jib / Crane': [
      {
        id: 'wed-jib-1',
        role: 'Jib / Crane',
        eventType: 'Wedding',
        title: 'Bouquet Toss Overhead Arc • Reception',
        description: 'Crane position starting high above dance floor, tilting down as bride tosses floral bouquet.',
        category: 'BOUQUET TOSS',
        focalLength: '18mm Wide',
        movement: 'Downward Plunge Arc',
        composition: 'Overhead view of bride throwing, arc tracking bouquet into cheering crowd of guests.',
        viewfinderType: 'crane-swoop',
        voiceScript: 'Jib, overhead arc for the bouquet toss in 3, 2, 1... track the toss... take Jib!',
        durationSeconds: 20,
      },
    ],
    'Audience Reaction': [
      {
        id: 'wed-aud-1',
        role: 'Audience Reaction',
        eventType: 'Wedding',
        title: 'Parents Tearful Smile • Front Pew',
        description: 'Warm, candid portrait of parents wiping happy tears as couple kisses at altar.',
        category: 'FAMILY EMOTION',
        focalLength: '135mm Intimate Tele',
        movement: 'Candid Float & Hold',
        composition: 'Mother and father smiling together, soft out-of-focus ceremony background.',
        viewfinderType: 'two-shot',
        voiceScript: 'Four, grab the parents reaction in the front row right now... priceless smile, take 4!',
        durationSeconds: 20,
      },
    ],
  },
};

// Backwards-compatibility alias defaulting to Concert catalog
export const AI_SHOT_CATALOG = AI_SHOT_CATALOG_BY_EVENT['Concert'];

/**
 * Anti-repeat history tracking per camera (prevents repeating shots within 40 cycles).
 */
const recentShotsByCamera = new Map<number, string[]>();
const MAX_RECENT_HISTORY = 40;

/**
 * Returns all 100+ available shot ideas for a camera role and event type.
 */
export const getAllShotsForRole = (
  role: CameraRole,
  eventType?: EventType
): AiShotDefinition[] => {
  const ev = eventType || 'Concert';
  const eventGroup = AI_SHOT_CATALOG_BY_EVENT[ev] || AI_SHOT_CATALOG_BY_EVENT['Concert'];
  const eventShots = eventGroup[role] || [];
  const baseShots = COMPLETE_SHOT_CATALOG[role] || COMPLETE_SHOT_CATALOG['Roving Stage'] || [];

  // Combine and deduplicate by ID
  const seenIds = new Set<string>();
  const combined: AiShotDefinition[] = [];

  for (const shot of eventShots) {
    if (!seenIds.has(shot.id)) {
      seenIds.add(shot.id);
      combined.push(shot);
    }
  }

  for (const shot of baseShots) {
    if (!seenIds.has(shot.id)) {
      seenIds.add(shot.id);
      combined.push({
        ...shot,
        eventType: ev,
      });
    }
  }

  return combined;
};

/**
 * Returns a ShotSuggestion tailored for a specific Event Type and Camera Role,
 * choosing from the 100+ shot library with anti-repeat history protection.
 */
export const getAiShotSuggestion = (
  role: CameraRole,
  eventType: EventType = 'Concert',
  indexOrSeed?: number,
  targetCameraId: number = 1
): ShotSuggestion => {
  const pool = getAllShotsForRole(role, eventType);
  if (pool.length === 0) {
    // Fallback safe shot
    return {
      id: `ai-safe-${Date.now()}`,
      title: 'Balanced Stage Master',
      description: 'Establish clear subject composition and hold steady on air.',
      category: 'LIVE BROADCAST',
      durationSeconds: 20,
      isAiGenerated: true,
      targetCameraId,
      timestamp: Date.now(),
      mediaType: 'image',
      mediaUrl: 'viewfinder://foh-wide',
    };
  }

  let def: AiShotDefinition;

  if (typeof indexOrSeed === 'number') {
    const idx = Math.abs(indexOrSeed) % pool.length;
    def = pool[idx];
  } else {
    // Random selection with anti-repeat history buffer
    if (!recentShotsByCamera.has(targetCameraId)) {
      recentShotsByCamera.set(targetCameraId, []);
    }
    const recent = recentShotsByCamera.get(targetCameraId)!;

    // Filter out recently shown shots
    const available = pool.filter((s) => !recent.includes(s.id));
    const selectionPool = available.length > 0 ? available : pool;

    const randomIdx = Math.floor(Math.random() * selectionPool.length);
    def = selectionPool[randomIdx];

    // Update history
    recent.push(def.id);
    if (recent.length > MAX_RECENT_HISTORY) {
      recent.shift();
    }
  }

  return {
    id: `ai-${def.id}-${Date.now()}`,
    title: def.title,
    description: `${def.description}\n\n📐 ${def.composition} • 🔭 ${def.focalLength} • 🎥 ${def.movement}`,
    category: `${eventType.toUpperCase()} • ${def.category}`,
    durationSeconds: def.durationSeconds,
    isAiGenerated: true,
    targetCameraId,
    timestamp: Date.now(),
    mediaType: 'image',
    mediaUrl: `viewfinder://${def.viewfinderType}`,
  };
};

/**
 * Returns the full definition for a given shot ID or viewfinder type.
 */
export const findShotDefinition = (
  viewfinderTypeOrId?: string | null,
  eventType?: EventType
): AiShotDefinition | null => {
  if (!viewfinderTypeOrId) return null;
  const cleanKey = viewfinderTypeOrId.replace('viewfinder://', '');

  // 1. Search specified event type catalog
  if (eventType && AI_SHOT_CATALOG_BY_EVENT[eventType]) {
    for (const role of Object.keys(AI_SHOT_CATALOG_BY_EVENT[eventType]) as CameraRole[]) {
      for (const def of AI_SHOT_CATALOG_BY_EVENT[eventType][role]) {
        if (def.viewfinderType === cleanKey || def.id === cleanKey) {
          return def;
        }
      }
    }
  }

  // 2. Search across COMPLETE_SHOT_CATALOG (105+ shots per role)
  for (const role of Object.keys(COMPLETE_SHOT_CATALOG) as CameraRole[]) {
    for (const def of COMPLETE_SHOT_CATALOG[role]) {
      if (def.viewfinderType === cleanKey || def.id === cleanKey) {
        return def;
      }
    }
  }

  // 3. Fallback: search across all event types
  for (const ev of Object.keys(AI_SHOT_CATALOG_BY_EVENT) as EventType[]) {
    for (const role of Object.keys(AI_SHOT_CATALOG_BY_EVENT[ev]) as CameraRole[]) {
      for (const def of AI_SHOT_CATALOG_BY_EVENT[ev][role]) {
        if (def.viewfinderType === cleanKey || def.id === cleanKey) {
          return def;
        }
      }
    }
  }

  return null;
};
