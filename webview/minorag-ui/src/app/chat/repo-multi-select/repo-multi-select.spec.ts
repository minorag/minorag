import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RepoMultiSelect } from './repo-multi-select';

describe('RepoMultiSelect', () => {
  let component: RepoMultiSelect;
  let fixture: ComponentFixture<RepoMultiSelect>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepoMultiSelect]
    })
    .compileComponents();

    fixture = TestBed.createComponent(RepoMultiSelect);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
