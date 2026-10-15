/*
  Jet War - Arduino Pro Micro (ATmega32U4) controller
  USB CDC serial, 115200 baud. Buttons are wired between pin and GND (INPUT_PULLUP).

  DIGITAL PINS
    D2  = Turn left   (A)
    D3  = Turn right  (D)
    D4  = Fire        (Left mouse / Left Ctrl)
    D5  = Switch weapon (Right mouse / Left Alt)
    D6  = Switch camera (C)
    D7  = Start / Enter
    D8  = Exit / Esc
    D9  = Joystick push button (re-center the stick)
    (D0/D1 are hardware UART, kept free)

  ANALOG PINS
    A0 = Joystick X  (jet yaw / mouse X)
    A1 = Joystick Y  (jet pitch / mouse Y)
    A2 = Speed potentiometer (replaces Space speed control)
    Joystick and pot: outer pins to VCC(5V) and GND, wiper to analog pin.

  Packet (every 10 ms):  J,<buttonsMask>,<x>,<y>,<pot>\n
    x,y,pot = 0..1023. Bit0=D2 Bit1=D3 Bit2=D4 Bit3=D5 Bit4=D6 Bit5=D7 Bit6=D8 Bit7=D9
*/

const uint8_t BTN_PINS[8] = {2, 3, 4, 5, 6, 7, 8, 9};
const uint8_t PIN_X = A0, PIN_Y = A1, PIN_POT = A2;

const unsigned long SEND_MS = 10;
const unsigned long DEBOUNCE_MS = 15;

uint8_t stableState = 0, lastRaw = 0;
unsigned long lastChange[8] = {0};
unsigned long lastSend = 0;

uint16_t readAvg(uint8_t pin) {
  uint32_t s = 0;
  for (uint8_t i = 0; i < 4; i++) s += analogRead(pin);
  return s >> 2;
}

void setup() {
  for (uint8_t i = 0; i < 8; i++) pinMode(BTN_PINS[i], INPUT_PULLUP);
  Serial.begin(115200);
}

void loop() {
  unsigned long now = millis();

  // debounced buttons (pressed = LOW -> bit 1)
  for (uint8_t i = 0; i < 8; i++) {
    bool pressed = (digitalRead(BTN_PINS[i]) == LOW);
    bool prev = bitRead(lastRaw, i);
    if (pressed != prev) {
      lastChange[i] = now;
      bitWrite(lastRaw, i, pressed);
    }
    if ((now - lastChange[i]) >= DEBOUNCE_MS) bitWrite(stableState, i, bitRead(lastRaw, i));
  }

  if (now - lastSend >= SEND_MS) {
    lastSend = now;
    Serial.print("J,");
    Serial.print(stableState);
    Serial.print(',');
    Serial.print(readAvg(PIN_X));
    Serial.print(',');
    Serial.print(readAvg(PIN_Y));
    Serial.print(',');
    Serial.println(readAvg(PIN_POT));
  }
}
