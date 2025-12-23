import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ChatSettings } from './chat-settings';

describe('ChatSettings', () => {
  let component: ChatSettings;
  let fixture: ComponentFixture<ChatSettings>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ChatSettings],
    }).compileComponents();

    fixture = TestBed.createComponent(ChatSettings);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
