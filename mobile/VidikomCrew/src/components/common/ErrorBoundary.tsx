/**
 * src/components/common/ErrorBoundary.tsx
 * 
 * Production React Error Boundary for VidikomCrew.
 * Traps unhandled render crashes, records diagnostics, and presents a graceful
 * Material Design 3 recovery interface.
 */

import React, { Component, ErrorInfo, ReactNode } from 'react';
import {
  View,
  Text,
  TouchableOpacity,
  ScrollView,
  StyleSheet,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

export interface ErrorBoundaryProps {
  children: ReactNode;
  fallback?: ReactNode | ((props: { error: Error; resetError: () => void }) => ReactNode);
  onError?: (error: Error, errorInfo: ErrorInfo) => void;
  onReset?: () => void;
}

export interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
  errorInfo: ErrorInfo | null;
  showDetails: boolean;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  public state: ErrorBoundaryState = {
    hasError: false,
    error: null,
    errorInfo: null,
    showDetails: false,
  };

  public static getDerivedStateFromError(error: any): Partial<ErrorBoundaryState> {
    const normalizedError = error instanceof Error ? error : new Error(String(error));
    return {
      hasError: true,
      error: normalizedError,
    };
  }

  public componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    const normalizedError = error instanceof Error ? error : new Error(String(error));
    this.setState({ errorInfo });

    // Invoke external logger or telemetry hook
    if (typeof this.props.onError === 'function') {
      this.props.onError(normalizedError, errorInfo);
    }

    // Console logging with formatted stack
    console.error(
      '[VidikomCrew ErrorBoundary] Uncaught render exception caught:',
      normalizedError,
      errorInfo?.componentStack
    );
  }

  public resetError = (): void => {
    this.setState({
      hasError: false,
      error: null,
      errorInfo: null,
      showDetails: false,
    });

    if (typeof this.props.onReset === 'function') {
      this.props.onReset();
    }
  };

  private toggleDetails = (): void => {
    this.setState((prev) => ({ showDetails: !prev.showDetails }));
  };

  public render(): ReactNode {
    const { hasError, error, errorInfo, showDetails } = this.state;
    const { children, fallback } = this.props;

    if (!hasError) {
      return children;
    }

    // Custom fallback render prop or element
    if (fallback) {
      if (typeof fallback === 'function') {
        return fallback({ error: error || new Error('Unknown crash'), resetError: this.resetError });
      }
      return fallback;
    }

    return (
      <SafeAreaView style={styles.safeArea}>
        <View testID="error-boundary-container" style={styles.container}>
          <View testID="error-boundary-card" style={styles.card}>
            <View style={styles.headerRow}>
              <Text style={styles.alertIcon}>⚠️</Text>
              <Text testID="error-boundary-title" style={styles.title}>
                APPLICATION RECOVERY
              </Text>
            </View>

            <Text testID="error-boundary-message" style={styles.message}>
              An unexpected render exception was trapped by the VidikomCrew resilience engine.
              Tap below to reset the view and restore normal operation.
            </Text>

            {error && (
              <View style={styles.errorSummaryBox}>
                <Text style={styles.errorName}>{error.name || 'Error'}:</Text>
                <Text style={styles.errorMessage}>{error.message || 'Unknown render failure'}</Text>
              </View>
            )}

            <TouchableOpacity
              testID="error-boundary-retry-btn"
              accessibilityRole="button"
              accessibilityLabel="Reload Component"
              onPress={this.resetError}
              style={styles.retryBtn}
              activeOpacity={0.8}
            >
              <Text style={styles.retryBtnText}>RELOAD COMPONENT</Text>
            </TouchableOpacity>

            <TouchableOpacity
              testID="error-boundary-details-btn"
              accessibilityRole="button"
              accessibilityLabel="Toggle Technical Details"
              onPress={this.toggleDetails}
              style={styles.detailsToggleBtn}
            >
              <Text style={styles.detailsToggleText}>
                {showDetails ? 'HIDE TECHNICAL DETAILS ▲' : 'SHOW TECHNICAL DETAILS ▼'}
              </Text>
            </TouchableOpacity>

            {showDetails && (
              <View testID="error-boundary-details" style={styles.detailsContainer}>
                <ScrollView style={styles.detailsScroll} nestedScrollEnabled>
                  <Text style={styles.detailsText}>
                    {error?.stack || error?.message || 'No stack trace available.'}
                    {'\n\nComponent Stack:'}
                    {errorInfo?.componentStack || '\nNo component trace available.'}
                  </Text>
                </ScrollView>
              </View>
            )}
          </View>
        </View>
      </SafeAreaView>
    );
  }
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: '#0A0A0F',
  },
  container: {
    flex: 1,
    backgroundColor: '#0A0A0F',
    justifyContent: 'center',
    alignItems: 'center',
    padding: 16,
  },
  card: {
    width: '100%',
    maxWidth: 480,
    backgroundColor: '#15151C',
    borderRadius: 8,
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.1)',
    padding: 24,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.3,
    shadowRadius: 8,
    elevation: 6,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 16,
  },
  alertIcon: {
    fontSize: 24,
    marginRight: 10,
  },
  title: {
    fontSize: 18,
    fontWeight: '700',
    color: '#EF4444',
    letterSpacing: 1.0,
  },
  message: {
    fontSize: 14,
    color: '#9CA3AF',
    lineHeight: 20,
    marginBottom: 16,
  },
  errorSummaryBox: {
    backgroundColor: 'rgba(239, 68, 68, 0.08)',
    borderLeftWidth: 3,
    borderLeftColor: '#EF4444',
    padding: 12,
    borderRadius: 4,
    marginBottom: 20,
  },
  errorName: {
    fontSize: 13,
    fontWeight: '700',
    color: '#EF4444',
    marginBottom: 2,
  },
  errorMessage: {
    fontSize: 13,
    color: '#D1D5DB',
  },
  retryBtn: {
    backgroundColor: '#3B82F6',
    borderRadius: 6,
    paddingVertical: 12,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: 12,
  },
  retryBtnText: {
    color: '#FFFFFF',
    fontSize: 14,
    fontWeight: '700',
    letterSpacing: 0.8,
  },
  detailsToggleBtn: {
    paddingVertical: 8,
    alignItems: 'center',
  },
  detailsToggleText: {
    color: '#3B82F6',
    fontSize: 12,
    fontWeight: '600',
    letterSpacing: 0.5,
  },
  detailsContainer: {
    marginTop: 12,
    backgroundColor: '#050508',
    borderRadius: 4,
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.05)',
    maxHeight: 180,
    padding: 8,
  },
  detailsScroll: {
    flex: 1,
  },
  detailsText: {
    fontFamily: 'monospace',
    fontSize: 11,
    color: '#6B7280',
    lineHeight: 16,
  },
});

export default ErrorBoundary;
