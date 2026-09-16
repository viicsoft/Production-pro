import type { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import type { CompositeScreenProps, NavigatorScreenParams } from '@react-navigation/native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';

/**
 * 4 Primary tabs for VidikomCrew mobile broadcast client
 */
export type TabParamList = {
  Tally: undefined;
  Comms: undefined;
  Suggestions: undefined;
  ColorBalance: undefined;
  Settings: undefined;
};

/**
 * Root Stack parameter list for top-level navigation container
 */
export type RootStackParamList = {
  MainTabs: NavigatorScreenParams<TabParamList> | undefined;
};

/**
 * Composite navigation props for type-safe screen navigation
 */
export type TabScreenProps<T extends keyof TabParamList> = CompositeScreenProps<
  BottomTabScreenProps<TabParamList, T>,
  NativeStackScreenProps<RootStackParamList>
>;

export type TallyScreenProps = TabScreenProps<'Tally'>;
export type CommsScreenProps = TabScreenProps<'Comms'>;
export type ShotSuggestionsScreenProps = TabScreenProps<'Suggestions'>;
export type ColorBalanceScreenProps = TabScreenProps<'ColorBalance'>;
export type SettingsScreenProps = TabScreenProps<'Settings'>;

declare global {
  namespace ReactNavigation {
    interface RootParamList extends RootStackParamList {}
  }
}
