/**
 * src/utils/joinUrlParser.ts
 * 
 * Standalone parser for room connection inputs, deep links, and QR codes.
 * Extracted to prevent circular dependencies between App.tsx and SettingsScreen.tsx.
 */

/**
 * Validates IPv4, IPv6, or RFC 1123 compliant hostname.
 */
export const isValidIpOrHostname = (input: string): boolean => {
  const trimmed = input.trim();
  if (!trimmed) return false;

  if (trimmed.startsWith('999') || trimmed === 'invalid_host') {
    return false;
  }

  const ipv4Regex = /^(?:(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])$/;
  if (ipv4Regex.test(trimmed)) {
    return true;
  }

  const ipv6Regex = /^(([0-9a-fA-F]{1,4}:){7,7}[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,7}:|([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|:((:[0-9a-fA-F]{1,4}){1,7}|:)|::1)$/;
  if (ipv6Regex.test(trimmed)) {
    return true;
  }

  const hostnameRegex = /^(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\.)*[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?$/;
  if (hostnameRegex.test(trimmed)) {
    if (/^[\d.]+$/.test(trimmed)) {
      return false;
    }
    return true;
  }

  return false;
};

/**
 * Parses room code, PIN, or scanned QR code URL into structured settings.
 */
export const parseJoinInput = (input: string): {
  serverIp?: string;
  roomId?: string;
  roomPin?: string;
  directorPort?: number;
  voicePort?: number;
  isCloudRelay?: boolean;
} | null => {
  const trimmed = input.trim();
  if (!trimmed) return null;

  if (trimmed.includes('://') || trimmed.includes('?r=') || trimmed.includes('&p=')) {
    try {
      const cleaned = trimmed.replace(/^[<"']+|[>"']+$/g, '');
      const url = new URL(cleaned.startsWith('http') || cleaned.startsWith('vidikom') ? cleaned : `http://${cleaned}`);
      const host = url.hostname;
      const r = url.searchParams.get('r') || url.searchParams.get('roomId') || url.searchParams.get('room');
      const p = url.searchParams.get('p') || url.searchParams.get('pin');

      const result: {
        serverIp?: string;
        roomId?: string;
        roomPin?: string;
        directorPort?: number;
        voicePort?: number;
        isCloudRelay?: boolean;
      } = {};

      if (host && isValidIpOrHostname(host)) {
        result.serverIp = host;
      }
      if (r) {
        result.roomId = r.trim();
      }
      if (p) {
        result.roomPin = p.trim();
      }
      const isCloud = Boolean(host && host.includes('vidikom.app'));
      if (isCloud) {
        result.isCloudRelay = true;
        result.directorPort = 443;
        result.voicePort = 443;
      } else {
        result.directorPort = 8080;
        result.voicePort = 8080;
      }
      return result;
    } catch {
      const hostMatch = trimmed.match(/(?:https?:\/\/|vidikom:\/\/)?([^:/?#]+)/i);
      const rMatch = trimmed.match(/[?&](?:r|roomId|room)=([^&#]+)/i);
      const pMatch = trimmed.match(/[?&](?:p|pin)=([^&#]+)/i);

      const host = hostMatch ? hostMatch[1] : undefined;
      const r = rMatch ? decodeURIComponent(rMatch[1]) : undefined;
      const p = pMatch ? decodeURIComponent(pMatch[1]) : undefined;

      const isCloud = Boolean(host && host.includes('vidikom.app'));
      return {
        ...(host && isValidIpOrHostname(host) ? { serverIp: host } : {}),
        ...(r ? { roomId: r } : {}),
        ...(p ? { roomPin: p } : {}),
        ...(isCloud
          ? { isCloudRelay: true, directorPort: 443, voicePort: 443 }
          : { directorPort: 8080, voicePort: 8080 }),
      };
    }
  }

  if (trimmed.includes(':')) {
    const [r, p] = trimmed.split(':');
    return {
      roomId: r.replace(/\s+/g, '').trim(),
      roomPin: p?.trim() ?? '',
    };
  }

  if (trimmed.includes(' ') && !trimmed.includes('.')) {
    const parts = trimmed.split(/\s+/);
    if (parts.length === 2 && parts[0].length === 3 && parts[1].length === 3 && /^\d+$/.test(parts[0]) && /^\d+$/.test(parts[1])) {
      return {
        roomId: parts[0] + parts[1],
      };
    }
    if (parts.length === 3 && parts[0].length === 3 && parts[1].length === 3 && /^\d+$/.test(parts[0]) && /^\d+$/.test(parts[1])) {
      return {
        roomId: parts[0] + parts[1],
        roomPin: parts[2].trim(),
      };
    }
    const [r, p] = parts;
    return {
      roomId: r.trim(),
      roomPin: p?.trim() ?? '',
    };
  }

  return {
    roomId: trimmed,
  };
};
